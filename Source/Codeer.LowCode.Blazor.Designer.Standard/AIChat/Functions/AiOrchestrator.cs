using Codeer.LowCode.Blazor.Designer.Extensibility;
using System.Text;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // 画面にバインドされる本体(旧: 画面ごとの XxxChat)。IAIChat として AIChatControl に渡される。
    // 役割:
    //  1. 初段ルーターでユーザー意図を機能に分解する。
    //  2. この画面で実行できる機能は順番に実行し、結果を集約する。
    //  3. この画面で実行できない機能は「対応不可(別画面で行える)」と案内する。
    internal class AiOrchestrator : IAIChat
    {
        readonly IntentRouter _router;
        // この画面で実行できる機能(ID → 実体)。履歴保持のためターンをまたいで使い回す。
        readonly Dictionary<string, IAiFunction> _functions;
        readonly IReadOnlyList<string> _screenFunctionIds;

        public AiOrchestrator(AiFunctionContext context, IReadOnlyList<string> screenFunctionIds)
        {
            _screenFunctionIds = screenFunctionIds;

            // 画面が対応するとされた機能のうち、実際にコンテキスト(エディタ)が能力を満たすものだけを生成する。
            _functions = new();
            foreach (var id in screenFunctionIds)
            {
                var fn = AiFunctionFactory.Create(id, context);
                if (fn != null) _functions[id] = fn;
            }

            _router = new IntentRouter(context.CreateChatClient, _functions.Keys.ToList());
        }

        public string Explanation
        {
            get
            {
                var names = _screenFunctionIds
                    .Where(id => FunctionCatalog.Entries.ContainsKey(id))
                    .Select(id => FunctionCatalog.Entries[id].DisplayName);
                return $"この画面では次のことができます: {string.Join(" / ", names)}。\r\n" +
                       "やりたいことを入力してください（複数まとめて指示してもOKです。担当機能に振り分けて順に処理します）。";
            }
        }

        public void Clear()
        {
            _router.Clear();
            foreach (var fn in _functions.Values) fn.Clear();
        }

        public async Task<string> ProcessMessage(string message)
        {
            // 1機能しか無い画面はルーターを飛ばして直接その機能へ(コスト・レイテンシ削減)。
            if (_functions.Count == 1 && _screenFunctionIds.Count == 1)
            {
                var only = _functions.Values.First();
                return (await only.ExecuteAsync(message)).Message;
            }

            var plan = await _router.RouteAsync(message);

            if (!string.IsNullOrWhiteSpace(plan.Question) && plan.Steps.Count == 0)
                return plan.Question;

            if (plan.Steps.Count == 0)
                return string.IsNullOrWhiteSpace(plan.Question)
                    ? "指示の内容を理解できませんでした。やりたいことをもう少し具体的に教えてください。"
                    : plan.Question;

            var single = plan.Steps.Count == 1;
            var sb = new StringBuilder();

            foreach (var step in plan.Steps)
            {
                var displayName = FunctionCatalog.Entries.TryGetValue(step.FunctionId, out var e) ? e.DisplayName : step.FunctionId;

                if (!_functions.TryGetValue(step.FunctionId, out var fn))
                {
                    // この画面では実行できない機能 → 対応画面を案内する。
                    var screens = FunctionCatalog.ScreensFor(step.FunctionId);
                    var where = screens.Count > 0 ? $"「{string.Join("」「", screens)}」画面で行えます。" : "別の画面で行ってください。";
                    AppendSection(sb, displayName, single,
                        $"この画面では実行できません。{where}\r\n（依頼内容: {step.Instruction}）");
                    continue;
                }

                var result = await fn.ExecuteAsync(step.Instruction);
                AppendSection(sb, displayName, single, result.Message);
            }

            return sb.ToString().TrimEnd();
        }

        // 単一ステップなら見出し無しでそのまま。複数ステップなら「【機能名】」を付けて区切る。
        static void AppendSection(StringBuilder sb, string displayName, bool single, string message)
        {
            if (single)
            {
                sb.Append(message);
                return;
            }
            if (sb.Length > 0) sb.Append("\r\n\r\n");
            sb.Append($"【{displayName}】\r\n{message}");
        }
    }
}

using Codeer.LowCode.Blazor.Json;
using Microsoft.Extensions.AI;
using System.Text;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // 初段ルーター。ユーザーの自由文の意図を機能単位に分解し、順序付きの実行プランを作る。
    // 実作業はしない(プランを立てるだけ)。曖昧なときは明確化のための質問を返す。
    // 全機能カタログで分類するため、この画面に無い機能へ振ることもある(その案内はオーケストレーターが行う)。
    internal class IntentRouter
    {
        readonly IChatClient _chatClient;
        readonly List<ChatMessage> _messages = new();
        readonly IReadOnlyList<string> _availableFunctionIds;

        public IntentRouter(Func<IChatClient> createChatClient, IReadOnlyList<string> availableFunctionIds)
        {
            _chatClient = createChatClient();
            _availableFunctionIds = availableFunctionIds;
        }

        public void Clear() => _messages.Clear();

        public async Task<RouterPlan> RouteAsync(string userMessage)
        {
            if (_messages.Count == 0)
                _messages.Add(new ChatMessage(ChatRole.System, BuildSystemPrompt()));

            _messages.Add(new ChatMessage(ChatRole.User, userMessage));

            try
            {
                var result = await _chatClient.GetResponseAsync(_messages,
                    new ChatOptions { ResponseFormat = ChatResponseFormat.Json });
                var resultText = result.Text;
                _messages.Add(new ChatMessage(ChatRole.Assistant, resultText));

                var plan = JsonConverterEx.DeserializeObject<RouterPlan>(resultText) ?? new RouterPlan();
                plan.Steps ??= new();
                // 未知の機能 ID を弾く(存在しない機能へは振らせない)。
                plan.Steps = plan.Steps
                    .Where(s => !string.IsNullOrEmpty(s.FunctionId) && FunctionCatalog.Entries.ContainsKey(s.FunctionId))
                    .ToList();
                return plan;
            }
            catch (Exception ex)
            {
                if (_messages.Count > 0 && _messages[^1].Role == ChatRole.User)
                    _messages.RemoveAt(_messages.Count - 1);
                // ルーティング自体が失敗した場合は、この画面が1機能だけならそれに丸投げ、複数なら質問扱い。
                if (_availableFunctionIds.Count == 1)
                    return new RouterPlan { Steps = new() { new RouterStep { FunctionId = _availableFunctionIds[0], Instruction = userMessage } } };
                return new RouterPlan { Question = $"指示の振り分けに失敗しました。もう一度お試しください。\r\n{ex.Message}" };
            }
        }

        string BuildSystemPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("あなたはローコードデザイナのAIアシスタントの「初段ルーター」です。");
            sb.AppendLine("ユーザーの指示が「何をしたいのか」を読み取り、下記の機能に振り分ける計画を JSON で返してください。あなた自身は作業しません(計画だけ)。");
            sb.AppendLine();
            sb.AppendLine("## 機能一覧(FunctionId: 説明)");
            foreach (var e in FunctionCatalog.Entries.Values)
                sb.AppendLine($"- {e.Id} ({e.DisplayName}): {e.RouterDescription}");
            sb.AppendLine();
            sb.AppendLine("## 今の画面で実際に実行できる機能(これ以外はユーザーに別画面を案内する)");
            sb.AppendLine(string.Join(", ", _availableFunctionIds.Select(id =>
                FunctionCatalog.Entries.TryGetValue(id, out var e) ? $"{id}({e.DisplayName})" : id)));
            sb.AppendLine();
            sb.AppendLine("## ルール");
            sb.AppendLine("- 指示を機能単位に分解し、Steps に「順番に」並べてください。依存関係がある場合は正しい順序にすること(例: フィールドを作ってから配置するなら field.create を先、layout.detail を後)。");
            sb.AppendLine("- **レイアウト機能(layout.*)はフィールドを一切作らず、既存フィールドを並べるだけ**です。並べる指示に、まだ存在しない部品(入力に付ける見出しラベル / 戻る・タイトル・サブミットボタン / 行番号 など)が必要なら、**先に field.create でそれらを作る Step を入れてから layout.* の Step を置いて**ください。既にその部品が存在するなら field.create は不要。");
            sb.AppendLine("- **【重要】詳細画面のフォーム整形(layout.detail)の既定**: 『いい感じに並べて』『見やすく配置』『フォームにして』のように詳細画面全体を整える指示では、既定で **各入力の見出しラベル + 画面タイトル + 戻るボタン + サブミットボタン** が必要です。これらが未作成なら、layout.detail の**前に** field.create の Step を必ず入れて『各データ項目に対応する見出しラベル(Text空+RelativeField)、画面タイトル、戻るボタン、サブミットボタンを作成』させてください(ユーザーが『ラベル不要』『戻る/サブミットは要らない』等と明示した場合を除く)。データ項目の追加も同時に頼まれたら、その field.create を先頭に置き、続けて(ラベル/パーツ作成の field.create →) layout.detail の順にします。");
            sb.AppendLine("  例: 詳細画面で『必要なフィールドを追加して、いい感じに並べて』→ Step1: field.create(必要なデータ項目を追加) → Step2: field.create(各入力の見出しラベル＋タイトル＋戻る＋サブミットを作成) → Step3: layout.detail(それらを並べる)。");
            sb.AppendLine("  例: 一覧で『行番号付きで並べて』→ field.create(行番号) → layout.list。");
            sb.AppendLine("- **レイアウト編集画面でのフィールド追加は「作成+配置」が既定**: 今の画面で layout.detail / layout.list / layout.search が実行できる場合、『○○フィールド(ボタン等)を追加して』という指示は、配置の明示が無くても **field.create の後にその画面の layout.* Step を続けて、作成したフィールドを現在のレイアウトへ配置**してください(レイアウト画面でフィールドを追加する意図は画面に表示すること。ユーザーが『配置は不要』『フィールド定義だけ』等と明示した場合を除く)。layout.* の Instruction には『既存のレイアウトは崩さず、新しく作成した○○を適切な位置に追加配置する』のように書くこと。");
            sb.AppendLine("  例: 詳細画面で『更新ボタンを追加してクリックイベントも追加』→ Step1: field.create(更新ボタン+OnClick) → Step2: layout.detail(既存レイアウトを保ったまま更新ボタンを追加配置)。");
            sb.AppendLine("- **イベント(OnClick等)の追加・設定は field.create(新規フィールドと同時)/ field.edit(既存フィールド)**へ振ってください(参照する空ハンドラ関数はスクリプトへ自動作成されます)。例: 『ボタンにクリックイベントを追加して』→ field.edit。**処理の中身まで指定された場合**(『クリックしたら○○するようにして』)は、field.edit の後に script.edit の Step を続けてください(script.edit はスクリプト画面でのみ実行可。ハンドラ名を Instruction に含めること)。");
            sb.AppendLine("- **フィールド作成/編集はデザインを変えるだけで物理DBには反映しません**。指示に『DBにテーブルを作成/DBに反映/ALTER/DBを更新/テーブル定義を変更/列をDBからも削除』等、**物理DB(テーブル・列)への反映**が含まれるなら、**field.create / field.edit の後に db.update の Step を必ず続けて**ください(db.update は全体設定でのみ実行可)。例: 『項目を追加してDBにもテーブルを作成して』→ Step1: field.create → Step2: db.update。『Testフィールドを削除してDBの列も削除』→ Step1: field.edit(削除) → Step2: db.update。");
            sb.AppendLine("- 各 Step の Instruction には、その機能に渡す具体的で自己完結した指示文を書いてください(元の指示のうち該当部分を、その機能だけで理解できる形に)。");
            sb.AppendLine("- FunctionId は上記一覧の ID のみを使うこと。今の画面で実行できない機能に該当する場合もその FunctionId で Step を作ってよい(実行できないことはツールがユーザーに案内します)。");
            sb.AppendLine("- 単一の機能で済む指示は Step 1つにする。無理に分割しない。");
            sb.AppendLine("- **対象は常に「今開いているモジュール/画面」です。『どのテーブル?』『どの対象?』『どのモジュール?』と聞き返さないでください**(対象は自明)。各機能は今の画面の対象に対して実行されます。");
            sb.AppendLine("- **今の画面の機能に素直に当てはまる指示は、聞き返さず即その機能へ振ってください**。例: 全体設定で『DDL書いて』『DDLを出して』『DDLを作って』『DBに反映して』『テーブルを追加/作成して』→ db.update。『テーブル名を付けて』『テーブル名を入れて』『(名前を)考えて入れて』→ module.settings。『○○フィールドを追加して』→ field.create。短い/口語的な指示でも、その画面の機能に無理なく対応するなら質問せず振り分けること。");
            sb.AppendLine("- 指示が曖昧で何をしたいか判断できないときだけ、Steps を空にして Question に確認質問を書いてください。判断できるなら質問せず Steps を返すこと(『やり方が2通り考えられるから確認』程度で質問しない。どれか妥当な解釈で進める)。");
            sb.AppendLine("- 挨拶や雑談など、どの機能にも当たらない場合は Steps を空にし、Question に軽く応答してよいか尋ねる/用件を聞く文を書いてください。");
            sb.AppendLine();
            sb.AppendLine("## 出力JSON形式");
            sb.AppendLine(@"{
  ""Question"": ""曖昧なときの確認質問。振り分けできるなら空文字"",
  ""Steps"": [ { ""FunctionId"": ""field.create"", ""Instruction"": ""..."" } ]
}");
            return sb.ToString();
        }
    }

    internal class RouterStep
    {
        public string FunctionId { get; set; } = string.Empty;
        public string Instruction { get; set; } = string.Empty;
    }

    internal class RouterPlan
    {
        // 空でなければ、オーケストレーターはこれをユーザーに返して次の入力を待つ。
        public string Question { get; set; } = string.Empty;
        public List<RouterStep> Steps { get; set; } = new();
    }
}

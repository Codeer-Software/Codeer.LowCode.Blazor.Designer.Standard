using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Microsoft.Extensions.AI;
using System.IO;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // css編集機能。旧 CssChat のロジックをそのまま機能ユニットへ移したもの。
    internal class CssFunction : IAiFunction
    {
        readonly ICssEditor _editor;
        readonly IChatClient _chatClient;
        readonly IDesignerChatHost _host;
        readonly List<ChatMessage> _messages = new();

        public string Id => FunctionCatalog.CssEdit;
        public string DisplayName => FunctionCatalog.Entries[FunctionCatalog.CssEdit].DisplayName;
        public string RouterDescription => FunctionCatalog.Entries[FunctionCatalog.CssEdit].RouterDescription;

        public CssFunction(IDesignerChatHost host, Func<IChatClient> createChatClient, ICssEditor editor)
        {
            _host = host;
            _editor = editor;
            _chatClient = createChatClient();
        }

        public void Clear() => _messages.Clear();

        public async Task<FunctionResult> ExecuteAsync(string instruction)
        {
            var currentCss = _editor.GetCss();

            // 仕様プロンプト(SystemPrompt + AppCss.md + デザイン情報)は会話の最初の1回だけ履歴に入れる。
            if (_messages.Count == 0)
            {
                _messages.Add(new ChatMessage(ChatRole.System, SystemPrompt));
                if (!string.IsNullOrEmpty(CssReference))
                    _messages.Add(new ChatMessage(ChatRole.System, 
                        "## アプリケーションのCSS仕様（DOM構造・セレクタ・スタイリングルール）\n\n" + CssReference));
                _messages.Add(new ChatMessage(ChatRole.System, BuildDesignContextInfo()));

                // 独自フィールドの CSS 仕様(描画DOM・class)が登録されていれば追加する(無ければ不変)。
                var customCss = FieldCatalogPrompt.BuildSectionReference(FieldDocSection.Css);
                if (!string.IsNullOrEmpty(customCss))
                    _messages.Add(new ChatMessage(ChatRole.System,
                        "## 独自フィールドのCSS仕様（追加されたフィールドの描画DOM・class）\n\n" + customCss));
            }

            // 現在のCSSと指示は毎ターン追加する(CSSは編集で変わるため常に最新を渡す)。
            _messages.Add(new ChatMessage(ChatRole.User, 
                $"現在のCSS:\n```css\n{currentCss}\n```\n\n指示: {instruction}"));

            try
            {
                var result = await _chatClient.GetResponseAsync(_messages,
                    new ChatOptions { ResponseFormat = ChatResponseFormat.Json });
                var resultText = result.Text;

                var response = JsonConverterEx.DeserializeObject<AIResponse>(resultText)!;
                _editor.Update(response.Css);

                _messages.Add(new ChatMessage(ChatRole.Assistant, resultText));

                return FunctionResult.Done(string.IsNullOrEmpty(response.Explanation)
                    ? "CSSを変更しました"
                    : response.Explanation);
            }
            catch (Exception ex)
            {
                // 失敗したターンのユーザーメッセージは履歴に残さない(次回の再送・文脈汚染を防ぐ)。
                if (_messages.Count > 0 && _messages[^1].Role == ChatRole.User)
                    _messages.RemoveAt(_messages.Count - 1);
                return FunctionResult.Error($"エラーリトライしてください\r\n{ex.Message}");
            }
        }

        string BuildDesignContextInfo()
        {
            var lines = new List<string> { "## 現在のアプリケーション情報" };

            try
            {
                var designData = _host.GetDesignData();

                var moduleNames = designData.Modules.GetModuleNames();
                if (moduleNames.Any())
                {
                    lines.Add("\n### モジュール一覧（data-module / list-module セレクタで使用可能）");
                    foreach (var name in moduleNames)
                    {
                        var mod = designData.Modules.Find(name);
                        if (mod == null) continue;

                        var fieldNames = mod.Fields.Select(f => f.Name).ToList();
                        var classNames = new List<string>();

                        foreach (var kvp in mod.DetailLayouts)
                        {
                            if (!string.IsNullOrEmpty(kvp.Value.ClassName))
                                classNames.Add($"DetailLayout(\"{kvp.Key}\"): {kvp.Value.ClassName}");

                            CollectClassNames(kvp.Value.Layout, classNames);
                        }

                        lines.Add($"- {name}");
                        if (fieldNames.Any())
                            lines.Add($"  フィールド: {string.Join(", ", fieldNames)}");
                        if (classNames.Any())
                            lines.Add($"  ClassName: {string.Join(", ", classNames)}");
                    }
                }

                var pageFrameNames = designData.PageFrames.GetPageFrameNames();
                if (pageFrameNames.Any())
                {
                    lines.Add("\n### ページフレーム一覧（data-pageframe セレクタで使用可能）");
                    foreach (var name in pageFrameNames)
                    {
                        lines.Add($"- {name}");
                    }
                }
            }
            catch
            {
                lines.Add("（デザインデータの取得に失敗しました）");
            }

            return string.Join("\n", lines);
        }

        static void CollectClassNames(LayoutDesignBase? layout, List<string> classNames)
        {
            if (layout == null) return;

            if (layout is FieldLayoutDesign field)
            {
                if (!string.IsNullOrEmpty(field.ClassName))
                    classNames.Add(field.ClassName);
            }
            else if (layout is GridLayoutDesign grid)
            {
                foreach (var row in grid.Rows)
                    foreach (var col in row.Columns)
                        CollectClassNames(col.Layout, classNames);
            }
            else if (layout is TabLayoutDesign tab)
            {
                foreach (var tabLayout in tab.Layouts)
                    CollectClassNames(tabLayout, classNames);
            }
        }

        // CSSの仕様知識(DOM構造・セレクタ・スタイリングルール)は Lib/AI/AppCss.md を埋め込みリソースとして読み込む。
        static readonly string CssReference = EmbeddedDocs.Spec("AppCss");

        class AIResponse
        {
            public string Css { get; set; } = string.Empty;
            public string Explanation { get; set; } = string.Empty;
        }

        const string SystemPrompt = @"
あなたはローコードWebアプリケーションのCSSエディタです。
ユーザーの指示に基づいてapp.cssを編集し、結果をJSONで返してください。

## 基本ルール
- 元のCSSが渡されるので、ユーザーの指示に対して**必要最小限の変更**にしてください。
- 既存のCSSルールは指示がない限り変更・削除しないでください。
- 新しいルールは既存CSSの末尾に追加してください。
- コメントがある場合はそのまま保持してください。
- 別途渡される「## アプリケーションのCSS仕様」に記載されたDOM構造・セレクタ・CSS変数・スタイリングルールに必ず従ってください。

## 出力JSON形式

以下の形式でJSONを返してください:
{
  ""Css"": ""/* 変更後のCSS全体 */"",
  ""Explanation"": ""変更内容の説明""
}

- Css: 変更後のCSS全体を文字列として返す。改行は \n で表現する。
- Explanation: 何を変更したかの簡潔な日本語説明。
";
    }
}

using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Microsoft.Extensions.AI;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // スクリプト編集機能。旧 ScriptChat のロジックを機能ユニットへ移したもの。
    // 詳細/一覧/検索レイアウト画面・スクリプト画面から共通で利用される。
    internal class ScriptFunction : IAiFunction
    {
        readonly IScriptContext _editor;
        readonly IChatClient _chatClient;
        readonly IDesignerChatHost _host;
        readonly List<ChatMessage> _messages = new();

        public string Id => FunctionCatalog.ScriptEdit;
        public string DisplayName => FunctionCatalog.Entries[FunctionCatalog.ScriptEdit].DisplayName;
        public string RouterDescription => FunctionCatalog.Entries[FunctionCatalog.ScriptEdit].RouterDescription;

        public ScriptFunction(IDesignerChatHost host, Func<IChatClient> createChatClient, IScriptContext editor)
        {
            _host = host;
            _editor = editor;
            _chatClient = createChatClient();
        }

        public void Clear() => _messages.Clear();

        public async Task<FunctionResult> ExecuteAsync(string instruction)
        {
            var currentScript = _editor.GetScript();

            // 仕様プロンプト(SystemPrompt + スクリプト仕様Docs + モジュール情報)は会話の最初の1回だけ履歴に入れる。
            if (_messages.Count == 0)
            {
                _messages.Add(new ChatMessage(ChatRole.System, SystemPrompt));
                if (!string.IsNullOrEmpty(ScriptReference))
                    _messages.Add(new ChatMessage(ChatRole.System, 
                        "## スクリプト仕様（言語仕様・Module/Field API・組み込み/拡張サービス・規約）\n\n" + ScriptReference));
                var context = BuildModuleContextInfo();
                if (!string.IsNullOrEmpty(context))
                    _messages.Add(new ChatMessage(ChatRole.System, context));

                // この環境に実際に登録されているスクリプトオブジェクト(サービス/型/列挙型 + 登録ドキュメント)。
                // 静的な ScriptExtensions.md の代わりに ScriptObjectCatalog から動的生成する
                // (Extras / 独自ライブラリの追加・未導入が正確に反映される)。
                var scriptObjects = ScriptObjectCatalogPrompt.Build();
                if (!string.IsNullOrEmpty(scriptObjects))
                    _messages.Add(new ChatMessage(ChatRole.System, scriptObjects));

                // 独自フィールドのスクリプトAPI(公開メンバの挙動・例)が登録されていれば追加する(無ければ不変)。
                var customScript = FieldCatalogPrompt.BuildSectionReference(FieldDocSection.Script);
                if (!string.IsNullOrEmpty(customScript))
                    _messages.Add(new ChatMessage(ChatRole.System,
                        "## 独自フィールドのスクリプトAPI（追加されたフィールドのメソッド・プロパティの挙動と例）\n\n" + customScript));
            }

            // 現在のスクリプトと指示は毎ターン追加する(スクリプトは編集で変わるため常に最新を渡す)。
            _messages.Add(new ChatMessage(ChatRole.User, 
                $"現在のスクリプト:\n```csharp\n{currentScript}\n```\n\n指示: {instruction}"));

            // 生成 → デザインチェック → エラーがあればAIに返して再生成、を上限まで繰り返す自己修正ループ。
            const int maxAttempts = 3;
            var lastScript = string.Empty;
            var lastExplanation = string.Empty;
            var lastErrors = new List<DesignCheckInfo>();

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                AIResponse response;
                string resultText;
                try
                {
                    var result = await _chatClient.GetResponseAsync(_messages,
                        new ChatOptions { ResponseFormat = ChatResponseFormat.Json });
                    resultText = result.Text;
                    response = JsonConverterEx.DeserializeObject<AIResponse>(resultText)!;
                }
                catch (Exception ex)
                {
                    if (_messages.Count > 0 && _messages[^1].Role == ChatRole.User)
                        _messages.RemoveAt(_messages.Count - 1);
                    return FunctionResult.Error($"エラーリトライしてください\r\n{ex.Message}");
                }

                _messages.Add(new ChatMessage(ChatRole.Assistant, resultText));
                lastScript = response.Script;
                lastExplanation = response.Explanation;

                var errors = ValidateScript(response.Script);
                if (errors.Count == 0)
                {
                    _editor.Update(response.Script);
                    return FunctionResult.Done(string.IsNullOrEmpty(response.Explanation)
                        ? "スクリプトを変更しました"
                        : response.Explanation);
                }

                lastErrors = errors;
                if (attempt < maxAttempts)
                    _messages.Add(new ChatMessage(ChatRole.User, 
                        "生成されたスクリプトに次のデザインチェックエラーがあります。修正してスクリプト全体を再度出力してください。\n"
                        + FormatErrors(errors)));
            }

            // 上限まで試してもエラーが残った: 最後の生成を適用しつつ警告する(ユーザーが手で直せるように)。
            _editor.Update(lastScript);
            var head = string.IsNullOrEmpty(lastExplanation) ? "スクリプトを変更しました" : lastExplanation;
            return FunctionResult.Done($"{head}\r\n（注意: デザインチェックエラーが残っています。内容を確認してください）\r\n{FormatErrors(lastErrors)}");
        }

        List<DesignCheckInfo> ValidateScript(string script)
        {
            try
            {
                return _editor.CheckScript(script);
            }
            catch
            {
                return new();
            }
        }

        static string FormatErrors(List<DesignCheckInfo> errors)
            => string.Join("\n", errors.Select(e => $"- {e.GetPositionText()}: {e.Message}"));

        string BuildModuleContextInfo()
        {
            try
            {
                var designData = _host.GetDesignData();
                var lines = new List<string>();

                var allModuleNames = designData.Modules.GetModuleNames();
                if (allModuleNames.Count > 0)
                {
                    lines.Add("## プロジェクト内の全モジュール一覧");
                    lines.Add("ダイアログ表示（new ModuleName()）やModuleSearcher<ModuleName>()で使用可能なモジュール:");
                    foreach (var name in allModuleNames)
                    {
                        var m = designData.Modules.Find(name);
                        if (m == null) continue;
                        var fieldNames = m.Fields.Select(f => f.Name).ToList();
                        var fieldSummary = fieldNames.Count > 0
                            ? $" - フィールド: {string.Join(", ", fieldNames)}"
                            : "";
                        lines.Add($"- {name}{fieldSummary}");
                    }
                    lines.Add("");
                }

                var moduleName = GetModuleName();
                if (!string.IsNullOrEmpty(moduleName))
                {
                    var mod = designData.Modules.Find(moduleName);
                    if (mod != null)
                    {
                        lines.Add($"## 現在編集中のモジュール: {moduleName}");

                        if (mod.Fields.Count > 0)
                        {
                            lines.Add("\n### フィールド一覧");
                            foreach (var f in mod.Fields)
                            {
                                var typeName = f.GetType().Name.Replace("Design", "");
                                var extra = GetFieldExtra(f);
                                lines.Add(extra.Length > 0
                                    ? $"- {f.Name} ({typeName}) {extra}"
                                    : $"- {f.Name} ({typeName})");
                            }
                        }

                        foreach (var kvp in mod.DetailLayouts)
                        {
                            var layoutKey = string.IsNullOrEmpty(kvp.Key) ? "デフォルト" : kvp.Key;
                            var events = new List<string>();
                            if (!string.IsNullOrEmpty(kvp.Value.OnBeforeInitialization))
                                events.Add($"OnBeforeInitialization: {kvp.Value.OnBeforeInitialization}");
                            if (!string.IsNullOrEmpty(kvp.Value.OnAfterInitialization))
                                events.Add($"OnAfterInitialization: {kvp.Value.OnAfterInitialization}");
                            if (!string.IsNullOrEmpty(kvp.Value.OnLocationChanging))
                                events.Add($"OnLocationChanging: {kvp.Value.OnLocationChanging}");
                            if (!string.IsNullOrEmpty(kvp.Value.OnFieldDataChanged))
                                events.Add($"OnFieldDataChanged: {kvp.Value.OnFieldDataChanged}");
                            if (events.Count > 0)
                            {
                                lines.Add($"\n### DetailLayout({layoutKey})のイベント");
                                lines.AddRange(events.Select(e => $"- {e}"));
                            }
                        }

                        foreach (var kvp in mod.ListLayouts)
                        {
                            var layoutKey = string.IsNullOrEmpty(kvp.Key) ? "デフォルト" : kvp.Key;
                            var events = new List<string>();
                            if (!string.IsNullOrEmpty(kvp.Value.OnBeforeInitialization))
                                events.Add($"OnBeforeInitialization: {kvp.Value.OnBeforeInitialization}");
                            if (!string.IsNullOrEmpty(kvp.Value.OnAfterInitialization))
                                events.Add($"OnAfterInitialization: {kvp.Value.OnAfterInitialization}");
                            if (!string.IsNullOrEmpty(kvp.Value.OnFieldDataChanged))
                                events.Add($"OnFieldDataChanged: {kvp.Value.OnFieldDataChanged}");
                            if (events.Count > 0)
                            {
                                lines.Add($"\n### ListLayout({layoutKey})のイベント");
                                lines.AddRange(events.Select(e => $"- {e}"));
                            }
                        }

                        foreach (var kvp in mod.SearchLayouts)
                        {
                            var layoutKey = string.IsNullOrEmpty(kvp.Key) ? "デフォルト" : kvp.Key;
                            if (!string.IsNullOrEmpty(kvp.Value.OnSearchInitialization))
                            {
                                lines.Add($"\n### SearchLayout({layoutKey})のイベント");
                                lines.Add($"- OnSearchInitialization: {kvp.Value.OnSearchInitialization}");
                            }
                        }

                        var relatedModules = new HashSet<string>();
                        foreach (var f in mod.Fields)
                        {
                            var searchModuleName = GetSearchModuleName(f);
                            if (!string.IsNullOrEmpty(searchModuleName))
                                relatedModules.Add(searchModuleName);
                        }
                        if (relatedModules.Count > 0)
                            lines.Add($"\n### 関連モジュール: {string.Join(", ", relatedModules)}");
                    }
                }

                return lines.Count > 0 ? string.Join("\n", lines) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        string? GetModuleName()
        {
            var name = _editor.GetModuleName();
            return string.IsNullOrEmpty(name) ? null : name;
        }

        static string GetFieldExtra(FieldDesignBase field)
        {
            var parts = new List<string>();
            AddEventIfSet(field, "OnClick", parts);
            AddEventIfSet(field, "OnDataChanged", parts);
            AddEventIfSet(field, "OnSearchDataChanged", parts);
            AddEventIfSet(field, "OnSearchButtonClicked", parts);
            AddEventIfSet(field, "OnSearched", parts);
            AddEventIfSet(field, "OnSelectedIndexChanged", parts);
            AddEventIfSet(field, "OnSelectedIndexChanging", parts);
            AddEventIfSet(field, "OnTransaction", parts);
            return parts.Count > 0 ? $"[{string.Join(", ", parts)}]" : string.Empty;
        }

        static void AddEventIfSet(FieldDesignBase field, string propertyName, List<string> parts)
        {
            var prop = field.GetType().GetProperty(propertyName);
            if (prop == null) return;
            var value = prop.GetValue(field) as string;
            if (!string.IsNullOrEmpty(value))
                parts.Add($"{propertyName}: {value}");
        }

        static string? GetSearchModuleName(FieldDesignBase field)
        {
            var prop = field.GetType().GetProperty("SearchCondition");
            if (prop == null) return null;
            var condition = prop.GetValue(field);
            if (condition == null) return null;
            var moduleNameProp = condition.GetType().GetProperty("ModuleName");
            var moduleName = moduleNameProp?.GetValue(condition) as string;
            return string.IsNullOrEmpty(moduleName) ? null : moduleName;
        }

        // スクリプト仕様(言語仕様・Module/Field API・規約)は埋め込みの各 .md を連結して読み込む。
        // 拡張サービス(Excel / WebApi / Toaster / Mail 等)は静的 md ではなく
        // ScriptObjectCatalogPrompt(登録済みスクリプトオブジェクトの動的カタログ)が担う。
        static readonly string ScriptReference = EmbeddedDocs.Load(
            "Codeer.LowCode.Blazor.Designer.Standard.AIChat.Scripts.md",
            "Codeer.LowCode.Blazor.Designer.Standard.AIChat.ScriptGuidelines.md",
            "Codeer.LowCode.Blazor.Designer.Standard.AIChat._ScriptApi.md");

        class AIResponse
        {
            public string Script { get; set; } = string.Empty;
            public string Explanation { get; set; } = string.Empty;
        }

        const string SystemPrompt = @"
あなたはローコードWebアプリケーションのスクリプトエディタです。
ユーザーの指示に基づいてC#ライクなスクリプト（*.mod.cs）を編集し、結果をJSONで返してください。

## 基本ルール
- 元のスクリプトが渡されるので、ユーザーの指示に対して**必要最小限の変更**にしてください。
- 既存のコードは指示がない限り変更・削除しないでください。
- 新しいメソッドは既存コードの末尾に追加してください。
- コメントがある場合はそのまま保持してください。
- 別途渡される「## スクリプト仕様」（言語仕様・Module/Field API・組み込み/拡張サービス・規約）に必ず従ってください。存在しないクラスやメソッドを生成しないこと。

## 出力JSON形式

{
  ""Script"": ""変更後のスクリプト全体"",
  ""Explanation"": ""変更内容の説明""
}

- Script: 変更後のスクリプト全体を文字列として返す。改行は \n で表現する。
- Explanation: 何を変更したかの簡潔な日本語説明。

## 主要フィールド型の固有API（クイックリファレンス。詳細は「## スクリプト仕様」を参照）

- **TextField**: Value(string?), SearchValue, SearchComparison
- **NumberField**: Value(decimal?), SearchMin, SearchMax
- **BooleanField**: Value(bool?)
- **DateField**: Value(DateOnly?), SearchMin, SearchMax
- **DateTimeField**: Value(DateTime?), SearchMin, SearchMax
- **TimeField**: Value(TimeOnly?), SearchMin, SearchMax
- **SelectField**: Value(string?), DisplayText, SetCandidates(...), ReloadCandidates(), SetAdditionalCondition(searcher)
- **LinkField**: Value(string?), DisplayText, SetAdditionalCondition(searcher)
- **LabelField**: Text(string)
- **ButtonField**: Text(string)
- **ListField/DetailListField**: Rows, SelectedIndex, Reload(), AddRow(), DeleteRow(), SetAdditionalCondition(searcher)
- **SearchField**: ExecuteSearch(), ExecuteClear()
- **FileField**: FileName, GetMemoryStream(), Download(), SetFile(name, content), ClearFile()
- **ModuleField**: ChildModule, SetModule(moduleName, layoutName)
- **ImageViewerField**: Base64Data, SetBase64Data(name, value)
- **ApexChartField/ApexRadialChartField**: AllowLoad(bool), Reload(), SetAdditionalCondition(searcher), AddAnnotation(name, annotation), RemoveAnnotation(name), ClearAnnotation()

## 列挙型
- TransactionMode: Insert, Update, Delete
- MatchComparison: Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual, Like, In, NotIn
- ModuleLayoutType: Detail, List, Search
- PanelAlignment: Left, Right
- MidpointRounding: AwayFromZero, ToEven
";
    }
}

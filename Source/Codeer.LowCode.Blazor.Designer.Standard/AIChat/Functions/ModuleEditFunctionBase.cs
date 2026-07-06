using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Microsoft.Extensions.AI;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // フィールド作成 / フィールド編集 / モジュール設定編集 の共通基盤。
    // 旧 OverallSettingsChat の「生成 → 他モジュール情報提供 → 未知プロパティ検証 → デザインチェック →
    // 適用/取り消し → 必要ならDDL提示」ループをそのまま集約し、機能ごとの差分は
    // ・SystemPrompt(何をしてよいか)
    // ・Apply(出力のどの部分を実際にモジュールへ反映するか＝作成/編集/設定の境界をコードで強制)
    // の2点だけに絞る。3機能とも同じ IO を受け取り、Apply で適用範囲を制御する。
    internal abstract class ModuleEditFunctionBase : IAiFunction
    {
        protected readonly IFieldContext Editor;
        protected readonly IDesignerChatHost Host;
        readonly IChatClient _chatClient;
        readonly List<ChatMessage> _messages = new();

        protected ModuleEditFunctionBase(IDesignerChatHost host, Func<IChatClient> createChatClient, IFieldContext editor)
        {
            Host = host;
            Editor = editor;
            _chatClient = createChatClient();
        }

        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public abstract string RouterDescription { get; }

        // 機能固有のシステムプロンプト(出力プロトコルの共通部分は SystemProtocol を連結する)。
        protected abstract string SystemPrompt { get; }

        // 生成結果 output を editingModule へ反映する。作成/編集/設定で反映範囲が異なる(境界の強制)。
        protected abstract void Apply(ModuleDesign editingModule, IO output);

        public void Clear() => _messages.Clear();

        public async Task<FunctionResult> ExecuteAsync(string instruction)
        {
            var designData = Host.GetDesignData();

            if (_messages.Count == 0)
            {
                _messages.Add(new ChatMessage(ChatRole.System, SystemPrompt + "\n\n" + SystemProtocol));
                if (!string.IsNullOrEmpty(ModuleReference))
                    _messages.Add(new ChatMessage(ChatRole.System, 
                        "## モジュール設定仕様（ModuleDesign構造・権限条件・フィールド共通基底・検索条件・システムフィールド・フィールド型カタログ・TypeFullName一覧）\n\n" + ModuleReference));
                _messages.Add(new ChatMessage(ChatRole.System, BuildDesignContextInfo(designData)));

                // 全フィールド型(組み込み + 外部ライブラリ + 独自)のコンパクトカタログを FieldCatalog から動的生成して渡す。
                // 用途1行は FieldDocs(## Design)冒頭・プロパティは reflection のその型固有分。手書きの型カタログは廃止しこれに一本化。
                var fieldTypeCatalog = FieldCatalogPrompt.BuildFieldTypeCatalog();
                if (!string.IsNullOrEmpty(fieldTypeCatalog))
                    _messages.Add(new ChatMessage(ChatRole.System, fieldTypeCatalog));
            }

            var editingModule = Editor.GetModuleDesign();
            _messages.Add(new ChatMessage(ChatRole.User,
                $"現在のモジュール設定(null は未設定。どのプロパティも編集してよい):\n{SerializeForView(BuildInput(editingModule))}\n\n指示: {instruction}"));

            return await GenerateAndApplyAsync(1, new(), new());
        }

        async Task<FunctionResult> GenerateAndApplyAsync(int attempt, List<string> providedModuleInfo, List<string> createdHandlers)
        {
            IO? output;
            string resultText;
            try
            {
                var result = await _chatClient.GetResponseAsync(_messages,
                    new ChatOptions { ResponseFormat = ChatResponseFormat.Json });
                resultText = result.Text;
                output = JsonConverterEx.DeserializeObject<IO>(resultText);
            }
            catch (Exception ex)
            {
                if (_messages.Count > 0 && _messages[^1].Role == ChatRole.User)
                    _messages.RemoveAt(_messages.Count - 1);
                return FunctionResult.Error($"エラーリトライしてください\r\n{ex.Message}");
            }

            _messages.Add(new ChatMessage(ChatRole.Assistant, resultText));

            if (output == null)
            {
                var parseError = AiJsonValidation.GetUnmappedMemberError<IO>(resultText) ?? "(JSONとして解釈できませんでした。出力が途中で切れていないか確認してください)";
                if (attempt < 3)
                {
                    _messages.Add(new ChatMessage(ChatRole.User, 
                        "有効なJSONが返りませんでした。次のエラーを解消し、出力JSON形式で設定全体を最後まで出力してください。\n" + parseError));
                    return await GenerateAndApplyAsync(attempt + 1, providedModuleInfo, createdHandlers);
                }
                return FunctionResult.Error($"有効な応答が得られなかったため、変更は適用していません。\r\n{parseError}");
            }

            output.NeedModuleInfo ??= new();
            output.ModuleDesign ??= new();
            output.ModuleDesign.Fields ??= new();
            output.ModuleDesign.UserWriteCondition ??= new();
            output.ModuleDesign.UserReadCondition ??= new();
            output.ModuleDesign.DataWriteCondition ??= new();
            output.ModuleDesign.DataReadCondition ??= new();

            if (output.NeedModuleInfo.Any())
            {
                var newInfo = output.NeedModuleInfo.Where(n => !providedModuleInfo.Contains(n)).Distinct().ToList();
                if (newInfo.Any())
                {
                    var designData = Host.GetDesignData();
                    var mods = new List<ModuleInfo>();
                    foreach (var modName in newInfo)
                    {
                        var mod = designData.Modules.Find(modName);
                        if (mod == null) continue;
                        var fieldNameAndTypes = new Dictionary<string, string>();
                        foreach (var field in mod.Fields)
                            fieldNameAndTypes[field.Name] = field.GetType().Name;
                        mods.Add(new ModuleInfo { Name = mod.Name, FieldNameAndTypes = fieldNameAndTypes });
                    }
                    _messages.Add(new ChatMessage(ChatRole.User, 
                        $"要求された他モジュールの情報です。これを踏まえて設定を出力してください。\n{JsonConverterEx.SerializeObject(mods)}"));
                    providedModuleInfo.AddRange(newInfo);
                    return await GenerateAndApplyAsync(attempt, providedModuleInfo, createdHandlers);
                }
            }

            var unmappedError = AiJsonValidation.GetUnmappedMemberError<IO>(resultText);
            if (unmappedError != null && attempt < 3)
            {
                _messages.Add(new ChatMessage(ChatRole.User, 
                    "生成されたJSONに、定義に存在しないプロパティが含まれています。" +
                    "存在しないプロパティに書いた値は無視され、設定に反映されません。" +
                    "次のエラーを解消し、正しいプロパティ名・構造で設定全体を再度出力してください。\n" + unmappedError));
                return await GenerateAndApplyAsync(attempt + 1, providedModuleInfo, createdHandlers);
            }

            var editingModule = Editor.GetModuleDesign();
            var backup = JsonConverterEx.DeserializeObject<ModuleDesign>(JsonConverterEx.SerializeObject(editingModule))!;

            // AI の変更で「新たに増えた」エラーだけを対象にする(ベースライン差分)。
            // 元々あった(AI が触っていない)デザインチェックエラーは、AI 編集を巻き添えでブロックしない。
            // Apply 前の状態にはユーザーの編集中の内容も含まれるため、それらのエラーも AI の責任にはしない。
            var baselineIssues = CheckModuleSafe(editingModule);

            Apply(editingModule, output);

            // AI がイベントプロパティ(OnClick等)に設定した関数がスクリプトに無ければ、
            // デザイナの「-> Create New」と同じ仕組みで空のハンドラを自動作成する。
            // デザインチェックの前に作成することで「関数が存在しない」エラーによる差し戻しを防ぐ。
            // (この後の試行で適用が取り消されても、作成済みの空ハンドラは無害なのでスクリプトに残る)
            createdHandlers.AddRange(EnsureEventHandlers(backup, editingModule));

            var allIssues = DesignCheckDiff.NewErrors(baselineIssues, CheckModuleSafe(editingModule));

            // 適用の可否には「致命的(読込不能)なエラー」だけを使う。
            // 外部リソース未作成(テーブル/列がまだDBに無い)や「CUDには主キーIdが必要」は致命的ではなく設計上の助言なので、
            // ユーザー指定の設定は適用したうえで注意として伝えるだけにする。
            // (テーブル名を入れたいだけなのに、Id未整備という別件で弾かれて何も設定されない、を防ぐ)。
            var blocking = allIssues
                .Where(e => !IsExternalResourceExistenceError(e, editingModule) && !IsPrimaryKeyAdvisory(e, editingModule))
                .ToList();
            var advisories = allIssues.Where(e => IsPrimaryKeyAdvisory(e, editingModule)).ToList();

            if (blocking.Count == 0)
            {
                Editor.UpdateModule();
                var explanation = string.IsNullOrEmpty(output.Explanation) ? "変更しました" : output.Explanation;
                if (createdHandlers.Any())
                    explanation += $"\r\n\r\nスクリプトに空のイベントハンドラを作成しました: {string.Join(", ", createdHandlers.Distinct())}\r\n処理の中身はスクリプト画面で実装してください(スクリプト画面のAIチャットに依頼もできます)。";
                if (advisories.Any())
                    explanation += "\r\n\r\n(次の点にご注意ください)\r\n" + FormatErrors(advisories);
                if (unmappedError != null)
                    explanation += $"\r\n(注意: 認識できないプロパティがあり無視しました: {unmappedError})";
                // フィールド作成/編集・モジュール設定はデザインのみ変更する。物理DBへの反映(DDL)は別機能 db.update(全体設定のみ)で行う。
                return FunctionResult.Done(explanation);
            }

            RestoreModule(editingModule, backup);

            if (attempt < 3)
            {
                _messages.Add(new ChatMessage(ChatRole.User,
                    "生成された設定に次のデザインチェックエラーがあります。修正して設定全体を再度出力してください。\n" + FormatErrors(blocking)));
                return await GenerateAndApplyAsync(attempt + 1, providedModuleInfo, createdHandlers);
            }

            var abortMessage = $"デザインチェックエラーを解消できなかったため、変更は適用していません(現在の設定は保持されます)。\r\n{FormatErrors(blocking)}";
            if (createdHandlers.Any())
                abortMessage += $"\r\n(試行中にスクリプトへ作成した空のイベントハンドラ {string.Join(", ", createdHandlers.Distinct())} はそのまま残っています)";
            return FunctionResult.NothingToDo(abortMessage);
        }

        // 現在のモジュール状態を AI に見せる用の IO(全設定 + 全フィールド)。適用範囲は Apply で制御する。
        protected IO BuildInput(ModuleDesign editingModule)
            => new IO
            {
                ModuleName = editingModule.Name,
                ModuleDesign = new ModuleDesignEditing
                {
                    DataSourceName = editingModule.DataSourceName,
                    DbTable = editingModule.DbTable,
                    CanCreate = editingModule.CanCreate,
                    CanUpdate = editingModule.CanUpdate,
                    CanDelete = editingModule.CanDelete,
                    UserWriteCondition = editingModule.UserWriteCondition,
                    UserReadCondition = editingModule.UserReadCondition,
                    DataWriteCondition = editingModule.DataWriteCondition,
                    DataReadCondition = editingModule.DataReadCondition,
                    Fields = editingModule.Fields,
                },
                NeedModuleInfo = new()
            };

        // --- 各機能の Apply が使う部品(境界の強制) ---

        // モジュール設定(DataSource/DbTable/CRUD/権限条件)だけを反映する。Fields は触らない。
        protected static void ApplySettings(ModuleDesign module, IO output)
        {
            module.DataSourceName = output.ModuleDesign.DataSourceName;
            module.DbTable = output.ModuleDesign.DbTable;
            module.CanCreate = output.ModuleDesign.CanCreate;
            module.CanUpdate = output.ModuleDesign.CanUpdate;
            module.CanDelete = output.ModuleDesign.CanDelete;
            module.UserWriteCondition = output.ModuleDesign.UserWriteCondition;
            module.UserReadCondition = output.ModuleDesign.UserReadCondition;
            module.DataWriteCondition = output.ModuleDesign.DataWriteCondition;
            module.DataReadCondition = output.ModuleDesign.DataReadCondition;
        }

        // 新規フィールドの追加のみ反映する(既存フィールドは名前一致で温存し、変更しない)。
        // 出力に含まれる「既存に無い Name」のフィールドだけを末尾に追加する。既存フィールドの並び・内容は保持。
        protected static void ApplyFieldsCreateOnly(ModuleDesign module, IO output)
        {
            var existingNames = new HashSet<string>(module.Fields.Select(f => f.Name));
            foreach (var f in output.ModuleDesign.Fields)
            {
                if (string.IsNullOrEmpty(f.Name) || existingNames.Contains(f.Name)) continue;
                module.Fields.Add(f);
                existingNames.Add(f.Name);
            }
        }

        // 既存フィールドの編集(プロパティ変更・リネーム・削除・並べ替え)を反映する。
        // AI には「既存フィールドを全て含めて返す(変更分だけ書き換える)」よう指示しており、出力の Fields で丸ごと置き換える。
        // これによりリネーム(Name 変更)や削除(返さない)も反映できる。新規追加は行わない前提(ルーターがフィールド作成へ振る)。
        protected static void ApplyAllFields(ModuleDesign module, IO output)
        {
            module.Fields = output.ModuleDesign.Fields;
        }

        // AI が今回の適用で設定・変更したイベントプロパティ(CandidateType.ScriptEvent の string プロパティ)を
        // backup との比較で検出し、参照する関数が無ければエディタ経由で空のハンドラを作成する。
        // 変更していない(以前からの)イベント値には触れない = ユーザーの既存の不整合を勝手に「修正」しない。
        // 戻り値は作成したハンドラの関数名。
        List<string> EnsureEventHandlers(ModuleDesign backup, ModuleDesign edited)
        {
            var created = new List<string>();
            foreach (var field in edited.Fields)
            {
                var backupField = backup.Fields.FirstOrDefault(f => f.Name == field.Name && f.GetType() == field.GetType());
                foreach (var prop in field.GetType().GetProperties())
                {
                    if (prop.PropertyType != typeof(string)) continue;
                    if (prop.GetCustomAttribute<DesignerAttribute>()?.CandidateType != CandidateType.ScriptEvent) continue;

                    var value = prop.GetValue(field) as string;
                    if (string.IsNullOrEmpty(value)) continue;
                    if (backupField != null && prop.GetValue(backupField) as string == value) continue;

                    try
                    {
                        if (Editor.EnsureFieldEventHandler(field.Name, prop.Name))
                            created.Add(value);
                    }
                    catch
                    {
                        // ハンドラ作成失敗はデザイン適用を巻き添えにしない(未存在なら後続のデザインチェックが検出する)。
                    }
                }
            }
            return created;
        }

        static void RestoreModule(ModuleDesign target, ModuleDesign backup)
        {
            target.DataSourceName = backup.DataSourceName;
            target.DbTable = backup.DbTable;
            target.CanCreate = backup.CanCreate;
            target.CanUpdate = backup.CanUpdate;
            target.CanDelete = backup.CanDelete;
            target.UserWriteCondition = backup.UserWriteCondition;
            target.UserReadCondition = backup.UserReadCondition;
            target.DataWriteCondition = backup.DataWriteCondition;
            target.DataReadCondition = backup.DataReadCondition;
            target.Fields = backup.Fields;
        }

        List<DesignCheckInfo> CheckModuleSafe(ModuleDesign module)
        {
            try
            {
                return Editor.CheckModule(module).ToList();
            }
            catch
            {
                return new();
            }
        }


        // 「データの作成・更新・削除を行うモジュールには主キー Id が必要」の助言エラー。
        // ModuleDesign.CheckDesign は CheckIdExistsForCUD(Name, Name, Fields) と memberName にモジュール名を渡すため、
        // このエラーだけ Location.Member == モジュール名 になる(他のモジュール系チェックは DbTable / UserReadCondition 等の nameof)。
        // 読込不能にする致命的エラーではなく設計上の助言なので、設定の適用はブロックせず注意として伝える。
        static bool IsPrimaryKeyAdvisory(DesignCheckInfo info, ModuleDesign module)
            => info is ModuleDesignCheckInfo m
               && m.Location.Module == module.Name
               && m.Location.Member == module.Name;

        static bool IsExternalResourceExistenceError(DesignCheckInfo info, ModuleDesign module)
        {
            if (info is ModuleDesignCheckInfo m
                && m.Location.Module == module.Name
                && (m.Location.Member == nameof(ModuleDesign.DbTable)
                    || m.Location.Member == nameof(ModuleDesign.DataSourceName)))
                return true;

            if (info is FieldDesignCheckInfo f
                && f.Location.Module == module.Name
                && f.Location.Member?.StartsWith("DbColumn", StringComparison.Ordinal) == true)
                return true;

            return false;
        }

        static string FormatErrors(List<DesignCheckInfo> errors)
            => string.Join("\n", errors.Select(e => $"- {e.GetPositionText()}: {e.Message}"));

        string BuildDesignContextInfo(DesignData designData)
        {
            var lines = new List<string> { "## 現在のアプリケーション情報" };

            try
            {
                var moduleNames = designData.Modules.GetModuleNames();
                if (moduleNames.Any())
                {
                    lines.Add("\n### モジュール一覧（LinkField等の参照先として使用可能）");
                    foreach (var name in moduleNames)
                    {
                        var mod = designData.Modules.Find(name);
                        if (mod == null) continue;
                        var fieldSummary = mod.Fields.Select(f => $"{f.Name}({f.GetType().Name})");
                        lines.Add($"- {name}: {string.Join(", ", fieldSummary)}");
                    }
                }

                var dataSources = Host.GetDataSources();
                if (dataSources.Any())
                {
                    lines.Add("\n### データソース一覧（DataSourceNameに指定可能）");
                    foreach (var ds in dataSources)
                        lines.Add($"- {ds.Name} ({ds.DataSourceType})");
                }
            }
            catch
            {
                lines.Add("（デザインデータの取得に失敗しました）");
            }

            return string.Join("\n", lines);
        }

        // モジュール設定仕様 Docs。
        static readonly string ModuleReference = EmbeddedDocs.Load(
            "Codeer.LowCode.Blazor.Designer.Standard.AIChat.ModuleDesign.md",
            "Codeer.LowCode.Blazor.Designer.Standard.AIChat._FieldCommon.md",
            "Codeer.LowCode.Blazor.Designer.Standard.AIChat.SearchConditions.md",
            "Codeer.LowCode.Blazor.Designer.Standard.AIChat.JsonAbstractTypeFullName.md");

        // 現在のモジュール設定を AI に見せる用のシリアライズ。null も省略せず出力する。
        static readonly JsonSerializerOptions ViewOptions = CreateViewOptions();

        static JsonSerializerOptions CreateViewOptions()
        {
            var o = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
            };
            o.Converters.Add(new JsonStringEnumConverter());
            o.Converters.AddJsonConverters();
            return o;
        }

        protected static string SerializeForView(object value) => JsonSerializer.Serialize(value, ViewOptions);

        // 3機能で共通の出力JSON形式(IO)の説明。各 SystemPrompt の末尾に連結する。
        protected const string SystemProtocol = @"## 出力JSON形式

{
  ""ModuleName"": ""モジュール名（変更しない、そのまま返す）"",
  ""ModuleDesign"": { /* ModuleDesignEditing - 設定全体。DataSourceName/DbTable/CanCreate/CanUpdate/CanDelete/各権限条件/Fields */ },
  ""NeedModuleInfo"": [ /* 他モジュールのフィールド構成が必要なときだけモジュール名を列挙(情報を入れて再リクエストされる)。不要なら[] */ ],
  ""Explanation"": ""変更内容の簡潔な日本語説明""
}

- 各フィールド定義・検索条件・値オブジェクトには完全修飾名 TypeFullName を必ず設定してください（一覧は「## モジュール設定仕様」のTypeFullNameルールを参照）。
- ModuleName は変更しないでください。
- フィールド名は Module 内で一意な PascalCase、DBカラム名は snake_case。int 型プロパティに小数(14.0 等)を書かないこと。
- DbTable/DataSourceName: あなたにはテーブル一覧は渡されません。ユーザーが指定した(新規作成予定を含む)名前はそのまま採用し、存在しないからと勝手に空へ戻さないでください。";

        public class ModuleDesignEditing
        {
            public string DataSourceName { get; set; } = string.Empty;
            public string DbTable { get; set; } = string.Empty;

            public bool CanCreate { get; set; } = true;
            public bool CanUpdate { get; set; } = true;
            public bool CanDelete { get; set; } = true;

            public ModuleMatchCondition UserWriteCondition { get; set; } = new();
            public ModuleMatchCondition UserReadCondition { get; set; } = new();
            public ModuleMatchCondition DataWriteCondition { get; set; } = new();
            public ModuleMatchCondition DataReadCondition { get; set; } = new();

            public List<FieldDesignBase> Fields { get; set; } = new();
        }

        public class IO
        {
            public string ModuleName { get; set; } = string.Empty;
            public ModuleDesignEditing ModuleDesign { get; set; } = new();
            public List<string> NeedModuleInfo { get; set; } = new();
            public string Explanation { get; set; } = string.Empty;
        }

        public class ModuleInfo
        {
            public string Name { get; set; } = string.Empty;
            public Dictionary<string, string> FieldNameAndTypes { get; set; } = new();
        }
    }
}

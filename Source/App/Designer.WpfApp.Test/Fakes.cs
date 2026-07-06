using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Designer.WpfApp;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat;
using Microsoft.Extensions.AI;

namespace Designer.WpfApp.Test
{
    /// <summary>与えた応答を順番に返すだけの IChatClient。送信されたリクエストを記録する(決定的テスト用)。</summary>
    public sealed class ScriptedChatClient : IChatClient
    {
        readonly Queue<string> _responses;
        public List<List<ChatMessage>> Requests { get; } = new();
        public ScriptedChatClient(params string[] responses) => _responses = new(responses);

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToList());
            var text = _responses.Count > 0 ? _responses.Dequeue() : string.Empty;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    /// <summary>IModuleDesigns のテスト用フェイク(保持したモジュール群を返すだけ)。</summary>
    public sealed class FakeModuleDesigns : IModuleDesigns
    {
        readonly List<ModuleDesign> _modules;
        public FakeModuleDesigns(IEnumerable<ModuleDesign> modules) => _modules = modules.ToList();
        public List<string> GetModuleNames() => _modules.Select(m => m.Name).ToList();
        public ModuleDesign? Find(string name) => _modules.FirstOrDefault(m => m.Name == name);
        public IModuleDesigns Clone() => new FakeModuleDesigns(_modules);
    }

    /// <summary>
    /// IModuleOverallSettingsEditor のテスト用フェイク。編集対象モジュールを保持し、AI の出力はここに適用される。
    /// CheckModule はデフォルトで「エラー無し」を返す(プロンプト品質の検証に集中するため)。
    /// 検証ゲートの挙動も試したい場合は CheckOverride を差し込む。
    /// </summary>
    public sealed class FakeOverallSettingsEditor : IModuleOverallSettingsEditor
    {
        public ModuleDesign Module { get; }
        public int UpdateCount { get; private set; }
        public Func<ModuleDesign, List<DesignCheckInfo>>? CheckOverride { get; set; }

        public FakeOverallSettingsEditor(ModuleDesign module) => Module = module;

        public ModuleDesign GetModuleDesign() => Module;
        public List<DesignCheckInfo> CheckModule(ModuleDesign module) => CheckOverride?.Invoke(module) ?? new();
        public void Update() => UpdateCount++;

        /// <summary>EnsureFieldEventHandler の呼び出し記録。デフォルトは「新規作成した(true)」を返す。</summary>
        public List<(string Field, string Property)> EnsuredEventHandlers { get; } = new();
        public Func<string, string, bool>? EnsureEventHandlerOverride { get; set; }

        public bool EnsureFieldEventHandler(string fieldName, string eventPropertyName)
        {
            EnsuredEventHandlers.Add((fieldName, eventPropertyName));
            return EnsureEventHandlerOverride?.Invoke(fieldName, eventPropertyName) ?? true;
        }
    }

    /// <summary>IDesignerChatHost のテスト用フェイク。設計情報を返し、DDL は記録するだけ(WPF を起動しない)。</summary>
    public sealed class FakeChatHost : IDesignerChatHost
    {
        readonly DesignData _designData;
        readonly List<DataSource> _dataSources;
        readonly Func<string, List<DbTableDefinition>> _dbInfoProvider;

        public List<(DataSource DataSource, string Ddl)> ShownDdls { get; } = new();

        public FakeChatHost(DesignData designData, List<DataSource> dataSources,
            Func<string, List<DbTableDefinition>>? dbInfoProvider = null)
        {
            _designData = designData;
            _dataSources = dataSources;
            _dbInfoProvider = dbInfoProvider ?? (_ => new());
        }

        public string CurrentFileDirectory { get; set; } = "";
        public DesignData GetDesignData() => _designData;
        public IReadOnlyList<DataSource> GetDataSources() => _dataSources;
        public List<DbTableDefinition> GetDbInfo(string dataSourceName) => _dbInfoProvider(dataSourceName);
        public void ShowDdl(DataSource dataSource, string ddl) => ShownDdls.Add((dataSource, ddl));
    }

    public sealed class FakeCssEditor : ICssEditor
    {
        public string Css { get; set; }
        public int UpdateCount { get; private set; }
        public FakeCssEditor(string css = "") => Css = css;
        public string GetCss() => Css;
        public void Update(string css) { Css = css; UpdateCount++; }
    }

    public sealed class FakeScriptEditor : IScriptEditor
    {
        readonly string _moduleName;
        public string Script { get; set; }
        public int UpdateCount { get; private set; }
        public Func<string, List<DesignCheckInfo>>? CheckOverride { get; set; }
        public FakeScriptEditor(string script = "", string moduleName = "TestModule") { Script = script; _moduleName = moduleName; }
        public string GetScript() => Script;
        public List<DesignCheckInfo> CheckScript(string script) => CheckOverride?.Invoke(script) ?? new();
        public void Update(string script) { Script = script; UpdateCount++; }
        public string GetModuleName() => _moduleName;
    }

    public sealed class FakePageFrameEditor : IPageFrameEditor
    {
        public PageFrameDesign PageFrame { get; }
        public int UpdateCount { get; private set; }
        public Func<PageFrameDesign, List<DesignCheckInfo>>? CheckOverride { get; set; }
        public FakePageFrameEditor(PageFrameDesign pageFrame) => PageFrame = pageFrame;
        public PageFrameDesign GetPageFrameDesign() => PageFrame;
        public List<DesignCheckInfo> CheckPageFrame(PageFrameDesign pageFrame) => CheckOverride?.Invoke(pageFrame) ?? new();
        public void Update() => UpdateCount++;
    }

    public sealed class FakeQueryEditor : IQueryEditor
    {
        readonly string _dataSourceName;
        readonly DataSource? _dataSource; //CreateDbAccessor 用。未指定なら実行チェックはスキップされる
        public string CurrentSql { get; set; }
        public string? AppliedSql { get; private set; }
        public List<DbParameterSetting> AppliedParameters { get; private set; } = new();
        public ModuleDesign? ModuleDesignOverride { get; set; }
        public FakeQueryEditor(string dataSourceName, string currentSql = "", DataSource? dataSource = null)
        {
            _dataSourceName = dataSourceName;
            CurrentSql = currentSql;
            _dataSource = dataSource;
        }
        public string GetDataSourceName() => _dataSourceName;
        public string GetCurrentSql() => CurrentSql;
        public IDbAccessor CreateDbAccessor()
            => _dataSource != null
                ? new DbAccessorFactory().Create(new[] { _dataSource })
                : throw new NotSupportedException("このフェイクには DataSource が設定されていません(実行チェックはスキップされます)。");
        public void ApplySqlAndParameters(string sql, List<DbParameterSetting> parameters) { AppliedSql = sql; AppliedParameters = parameters; }
        // 自フィールド編集能力(このテストでは未使用のスタブ)。
        public string GetFieldName() => "TestField";
        public string GetModuleName() => "TestModule";
        public ModuleDesign GetModuleDesign() => ModuleDesignOverride ?? new() { Name = "TestModule" };
        public List<DesignCheckInfo> CheckModule(ModuleDesign module) => new();
        public void Update() { }
        public bool EnsureFieldEventHandler(string fieldName, string eventPropertyName) => false;
    }

    public sealed class FakeDetailLayoutEditor : IModuleDetailLayoutEditor
    {
        readonly List<FieldDesignBase> _fields;
        readonly string _moduleName;
        public DetailLayoutDesign Detail { get; }
        public int UpdateCount { get; private set; }
        public FakeDetailLayoutEditor(List<FieldDesignBase> fields, GridLayoutDesign? initialLayout = null, string moduleName = "TestModule")
        {
            _fields = fields;
            _moduleName = moduleName;
            Detail = new DetailLayoutDesign { Layout = initialLayout ?? new GridLayoutDesign() };
        }
        public void Update() => UpdateCount++;
        public string GetLayoutName() => string.Empty;
        // レイアウト機能は GetModuleDesign().DetailLayouts[GetLayoutName()] を読むため、編集中の Detail を含めて返す。
        public ModuleDesign GetModuleDesign() => new() { Name = _moduleName, Fields = _fields, DetailLayouts = new() { [string.Empty] = Detail } };
        public List<DesignCheckInfo> CheckModule(ModuleDesign module) => new();
        public bool EnsureFieldEventHandler(string fieldName, string eventPropertyName) => false;
        // [Obsolete] な後方互換メンバ(インターフェイスに残っているため実装は必要)。
        public string GetModuleName() => _moduleName;
        public List<FieldDesignBase> GetFieldDesigns() => _fields;
    }

    public sealed class FakeSearchLayoutEditor : IModuleSearchLayoutEditor
    {
        readonly List<FieldDesignBase> _fields;
        readonly string _moduleName;
        public SearchLayoutDesign Search { get; }
        public int UpdateCount { get; private set; }
        public FakeSearchLayoutEditor(List<FieldDesignBase> fields, SearchGridLayoutDesign? initialLayout = null, string moduleName = "TestModule")
        {
            _fields = fields;
            _moduleName = moduleName;
            Search = new SearchLayoutDesign
            {
                Layout = initialLayout ?? new SearchGridLayoutDesign { Rows = { GridRow.CreateEmptyRow() } }
            };
        }
        public void Update() => UpdateCount++;
        public string GetLayoutName() => string.Empty;
        // レイアウト機能は GetModuleDesign().SearchLayouts[GetLayoutName()] を読むため、編集中の Search を含めて返す。
        public ModuleDesign GetModuleDesign() => new() { Name = _moduleName, Fields = _fields, SearchLayouts = new() { [string.Empty] = Search } };
        public List<DesignCheckInfo> CheckModule(ModuleDesign module) => new();
        public bool EnsureFieldEventHandler(string fieldName, string eventPropertyName) => false;
        // [Obsolete] な後方互換メンバ(インターフェイスに残っているため実装は必要)。
        public string GetModuleName() => _moduleName;
        public List<FieldDesignBase> GetFieldDesigns() => _fields;
    }

    public sealed class FakeListLayoutEditor : IModuleListLayoutEditor
    {
        readonly List<FieldDesignBase> _fields;
        readonly string _moduleName;
        public ListLayoutDesign List { get; }
        public int UpdateCount { get; private set; }
        public FakeListLayoutEditor(List<FieldDesignBase> fields, ListLayoutDesign? initialLayout = null, string moduleName = "TestModule")
        {
            _fields = fields;
            _moduleName = moduleName;
            List = initialLayout ?? new ListLayoutDesign();
        }
        public void Update() => UpdateCount++;
        public string GetLayoutName() => string.Empty;
        // レイアウト機能は GetModuleDesign().ListLayouts[GetLayoutName()] を読むため、編集中の List を含めて返す。
        public ModuleDesign GetModuleDesign() => new() { Name = _moduleName, Fields = _fields, ListLayouts = new() { [string.Empty] = List } };
        public List<DesignCheckInfo> CheckModule(ModuleDesign module) => new();
        public bool EnsureFieldEventHandler(string fieldName, string eventPropertyName) => false;
        // [Obsolete] な後方互換メンバ(インターフェイスに残っているため実装は必要)。
        public string GetModuleName() => _moduleName;
        public List<FieldDesignBase> GetFieldDesigns() => _fields;
    }

    public sealed class FakeExecuteSqlEditor : IExecuteSqlEditor
    {
        readonly DataSource _dataSource;
        public string CurrentSql { get; set; }
        public string? AppliedSql { get; private set; }
        public List<DbParameterSetting> AppliedParameters { get; private set; } = new();
        public FakeExecuteSqlEditor(DataSource dataSource, string currentSql = "") { _dataSource = dataSource; CurrentSql = currentSql; }
        public string GetDataSourceName() => _dataSource.Name;
        public string GetCurrentSql() => CurrentSql;
        public IDbAccessor CreateDbAccessor() => new DbAccessorFactory().Create(new[] { _dataSource });
        public void ApplySqlAndParameters(string sql, List<DbParameterSetting> parameters) { AppliedSql = sql; AppliedParameters = parameters; }
        // 自フィールド編集能力(このテストでは未使用のスタブ)。
        public string GetFieldName() => "TestField";
        public string GetModuleName() => "TestModule";
        public ModuleDesign GetModuleDesign() => new() { Name = "TestModule" };
        public List<DesignCheckInfo> CheckModule(ModuleDesign module) => new();
        public void Update() { }
        public bool EnsureFieldEventHandler(string fieldName, string eventPropertyName) => false;
    }
}

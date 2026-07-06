using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // AI 機能が要求する「能力」の契約。AI 層(Lib/AI)が所有する(Designer 拡張レイヤの面インターフェイスには置かない)。
    // 面インターフェイス(IExecuteSqlEditor 等)からはブリッジクラスでこれらに適合させて機能へ渡す。

    // フィールド作成/編集・モジュール設定編集の各機能が使う能力。
    internal interface IFieldContext
    {
        string GetModuleName();
        // 編集対象のライブ ModuleDesign(参照。Fields を直接 Add/Remove/変更してよい)。
        ModuleDesign GetModuleDesign();
        List<DesignCheckInfo> CheckModule(ModuleDesign module);
        // モジュール(フィールド定義含む)への変更を確定し UI を更新する。
        void UpdateModule();
        // フィールドのイベントプロパティ(CandidateType.ScriptEvent)が参照する関数がモジュールのスクリプトに
        // 無ければ、デザイナの「-> Create New」と同じ仕組みで空のハンドラを作成する。
        // 作成した場合は true。既に存在する・値が空・対象プロパティでない場合は false。
        bool EnsureFieldEventHandler(string fieldName, string eventPropertyName);
    }

    // 「この画面が編集する自分のフィールド1つだけ」を編集する能力(SQL/Query 画面が自分のフィールド設定を編集する用)。
    // モジュール全体のフィールド操作(他フィールドの作成/編集)はせず、GetFieldName() のフィールドだけを対象にする。
    internal interface IFieldSelfContext : IFieldContext
    {
        string GetFieldName();
    }

    // スクリプト編集機能が使う能力。
    internal interface IScriptContext
    {
        string GetModuleName();
        string GetScript();
        List<DesignCheckInfo> CheckScript(string script);
        void Update(string script);
    }

    // 面インターフェイスのメソッドをデリゲートで束ね、IFieldContext として機能へ渡すブリッジ。
    internal sealed class FieldContextBridge : IFieldContext
    {
        readonly Func<string> _getModuleName;
        readonly Func<ModuleDesign> _getModuleDesign;
        readonly Func<ModuleDesign, List<DesignCheckInfo>> _checkModule;
        readonly Action _updateModule;
        readonly Func<string, string, bool> _ensureFieldEventHandler;

        public FieldContextBridge(
            Func<string> getModuleName,
            Func<ModuleDesign> getModuleDesign,
            Func<ModuleDesign, List<DesignCheckInfo>> checkModule,
            Action updateModule,
            Func<string, string, bool> ensureFieldEventHandler)
        {
            _getModuleName = getModuleName;
            _getModuleDesign = getModuleDesign;
            _checkModule = checkModule;
            _updateModule = updateModule;
            _ensureFieldEventHandler = ensureFieldEventHandler;
        }

        public string GetModuleName() => _getModuleName();
        public ModuleDesign GetModuleDesign() => _getModuleDesign();
        public List<DesignCheckInfo> CheckModule(ModuleDesign module) => _checkModule(module);
        public void UpdateModule() => _updateModule();
        public bool EnsureFieldEventHandler(string fieldName, string eventPropertyName) => _ensureFieldEventHandler(fieldName, eventPropertyName);
    }

    // 面インターフェイスのメソッドをデリゲートで束ね、IFieldSelfContext として機能へ渡すブリッジ。
    internal sealed class FieldSelfContextBridge : IFieldSelfContext
    {
        readonly Func<string> _getModuleName;
        readonly Func<ModuleDesign> _getModuleDesign;
        readonly Func<ModuleDesign, List<DesignCheckInfo>> _checkModule;
        readonly Action _updateModule;
        readonly Func<string, string, bool> _ensureFieldEventHandler;
        readonly Func<string> _getFieldName;

        public FieldSelfContextBridge(
            Func<string> getModuleName,
            Func<ModuleDesign> getModuleDesign,
            Func<ModuleDesign, List<DesignCheckInfo>> checkModule,
            Action updateModule,
            Func<string, string, bool> ensureFieldEventHandler,
            Func<string> getFieldName)
        {
            _getModuleName = getModuleName;
            _getModuleDesign = getModuleDesign;
            _checkModule = checkModule;
            _updateModule = updateModule;
            _ensureFieldEventHandler = ensureFieldEventHandler;
            _getFieldName = getFieldName;
        }

        public string GetModuleName() => _getModuleName();
        public ModuleDesign GetModuleDesign() => _getModuleDesign();
        public List<DesignCheckInfo> CheckModule(ModuleDesign module) => _checkModule(module);
        public void UpdateModule() => _updateModule();
        public bool EnsureFieldEventHandler(string fieldName, string eventPropertyName) => _ensureFieldEventHandler(fieldName, eventPropertyName);
        public string GetFieldName() => _getFieldName();
    }

    // 面インターフェイスのメソッドをデリゲートで束ね、IScriptContext として機能へ渡すブリッジ。
    internal sealed class ScriptContextBridge : IScriptContext
    {
        readonly Func<string> _getModuleName;
        readonly Func<string> _getScript;
        readonly Func<string, List<DesignCheckInfo>> _checkScript;
        readonly Action<string> _update;

        public ScriptContextBridge(
            Func<string> getModuleName,
            Func<string> getScript,
            Func<string, List<DesignCheckInfo>> checkScript,
            Action<string> update)
        {
            _getModuleName = getModuleName;
            _getScript = getScript;
            _checkScript = checkScript;
            _update = update;
        }

        public string GetModuleName() => _getModuleName();
        public string GetScript() => _getScript();
        public List<DesignCheckInfo> CheckScript(string script) => _checkScript(script);
        public void Update(string script) => _update(script);
    }
}

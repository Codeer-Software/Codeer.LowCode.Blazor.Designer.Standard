using Codeer.LowCode.Blazor.Designer.Extensibility;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // 機能 ID とコンテキストから IAiFunction を生成する。
    // 画面(面)コントロールは自分の面インターフェイス(IExecuteSqlEditor 等)を1つだけ実装する。
    // フィールド操作/スクリプト編集のような横断能力は、面インターフェイスのメソッドをブリッジクラス
    // (FieldContextBridge / ScriptContextBridge)で AI 層の能力インターフェイス(IFieldContext / IScriptContext)へ
    // 適合させてから機能へ渡す。レイアウト/SQL/ページフレーム/css はその画面固有の面インターフェイスを直接使う。
    internal static class AiFunctionFactory
    {
        public static IAiFunction? Create(string functionId, AiFunctionContext ctx)
        {
            var host = ctx.Host;
            var createChatClient = ctx.CreateChatClient;
            var editor = ctx.Editor;

            switch (functionId)
            {
                case FunctionCatalog.FieldCreate:
                    return ToFieldContext(editor) is { } fc ? new FieldCreateFunction(host, createChatClient, fc) : null;
                case FunctionCatalog.FieldEdit:
                    return ToFieldContext(editor) is { } fe ? new FieldEditFunction(host, createChatClient, fe) : null;
                case FunctionCatalog.ModuleSettings:
                    return ToFieldContext(editor) is { } ms ? new ModuleSettingsFunction(host, createChatClient, ms) : null;
                case FunctionCatalog.ScriptEdit:
                    return ToScriptContext(editor) is { } sc ? new ScriptFunction(host, createChatClient, sc) : null;
                case FunctionCatalog.DbUpdate:
                    return ToFieldContext(editor) is { } db ? new DbUpdateFunction(host, createChatClient, db) : null;
                case FunctionCatalog.LayoutDetail:
                    return editor is IModuleDetailLayoutEditor ld ? new DetailLayoutFunction(createChatClient, ld) : null;
                case FunctionCatalog.LayoutList:
                    return editor is IModuleListLayoutEditor ll ? new ListLayoutFunction(createChatClient, ll) : null;
                case FunctionCatalog.LayoutSearch:
                    return editor is IModuleSearchLayoutEditor ls ? new SearchLayoutFunction(createChatClient, ls) : null;
                case FunctionCatalog.SqlEdit:
                    if (editor is IExecuteSqlEditor ex) return new ExecuteSqlFunction(host, createChatClient, ex);
                    if (editor is IQueryEditor q) return new QueryFunction(host, createChatClient, q);
                    return null;
                case FunctionCatalog.SqlFieldSettings:
                    return ToFieldSelfContext(editor) is { } sfs ? new SqlFieldSettingsFunction(host, createChatClient, sfs) : null;
                case FunctionCatalog.PageFrameEdit:
                    return editor is IPageFrameEditor pf ? new PageFrameFunction(host, createChatClient, pf) : null;
                case FunctionCatalog.CssEdit:
                    return editor is ICssEditor css ? new CssFunction(host, createChatClient, css) : null;
                default:
                    return null;
            }
        }

        // 面インターフェイスが提供するフィールド操作メソッドを IFieldContext へ適合させるブリッジを生成する。
        // フィールド作成/編集・モジュール設定編集に対応する画面(全体設定/詳細・一覧・検索レイアウト/SQL)を網羅する。
        public static IFieldContext? ToFieldContext(object editor) => editor switch
        {
            // モジュール系エディタ(全体設定/詳細・一覧・検索レイアウト)は共通基底 IModuleEditor で一括適合。
            IModuleEditor e => new FieldContextBridge(() => e.GetModuleDesign().Name, e.GetModuleDesign, e.CheckModule, e.Update, e.EnsureFieldEventHandler),
            _ => null,
        };

        // 面インターフェイスのメソッドを IFieldSelfContext(自分の1フィールドだけを編集)へ適合させるブリッジを生成する。
        // SQL/Query 画面が対象。汎用フィールド操作(他フィールド)はさせず、GetFieldName() のフィールドだけを編集する。
        public static IFieldSelfContext? ToFieldSelfContext(object editor) => editor switch
        {
            // 自フィールド編集エディタ(SQL/Query)は共通の IModuleFieldEditor で一括適合。
            IModuleFieldEditor e => new FieldSelfContextBridge(() => e.GetModuleDesign().Name, e.GetModuleDesign, e.CheckModule, e.Update, e.EnsureFieldEventHandler, e.GetFieldName),
            _ => null,
        };

        // 面インターフェイスが提供するスクリプト編集メソッドを IScriptContext へ適合させるブリッジを生成する。
        // スクリプト編集はスクリプト画面(IScriptEditor)だけが対象。モジュール/レイアウト画面からは扱わない
        // (スクリプトは別ドキュメントでライフサイクルが独立し、開いているスクリプトエディタがバッファを所有するため)。
        public static IScriptContext? ToScriptContext(object editor) => editor switch
        {
            IScriptEditor e => new ScriptContextBridge(e.GetModuleName, e.GetScript, e.CheckScript, e.Update),
            _ => null,
        };
    }
}

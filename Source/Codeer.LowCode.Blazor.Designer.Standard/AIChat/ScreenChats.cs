using Microsoft.Extensions.AI;
using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat
{
    // 画面ごとの AI チャットのエントリポイント。App.xaml.cs は従来どおりこれらのクラスに接続する。
    // 中身は「初段ルーター + 対応機能セットのオーケストレーション」(AiOrchestrator)へ分解済みで、
    // 各クラスは自画面の編集コンテキストと対応機能セットを与えるだけの薄いラッパー。
    //
    // editor は画面コントロールで、自画面の面インターフェイス(IExecuteSqlEditor 等)を1つ実装している。
    // 横断能力(フィールド操作/スクリプト編集)は AiFunctionFactory がブリッジで AI 層の能力インターフェイスへ適合させる。

    internal class OverallSettingsChat : AiOrchestrator
    {
        public OverallSettingsChat(IDesignerChatHost host, Func<IChatClient> createChatClient, IModuleOverallSettingsEditor editor)
            : base(new AiFunctionContext(host, createChatClient, editor), FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenOverallSettings]) { }
    }

    internal class DetailLayoutChat : AiOrchestrator
    {
        public DetailLayoutChat(IDesignerChatHost host, Func<IChatClient> createChatClient, IModuleDetailLayoutEditor editor)
            : base(new AiFunctionContext(host, createChatClient, editor), FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenDetailLayout]) { }
    }

    internal class ListLayoutChat : AiOrchestrator
    {
        public ListLayoutChat(IDesignerChatHost host, Func<IChatClient> createChatClient, IModuleListLayoutEditor editor)
            : base(new AiFunctionContext(host, createChatClient, editor), FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenListLayout]) { }
    }

    internal class SearchLayoutChat : AiOrchestrator
    {
        public SearchLayoutChat(IDesignerChatHost host, Func<IChatClient> createChatClient, IModuleSearchLayoutEditor editor)
            : base(new AiFunctionContext(host, createChatClient, editor), FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenSearchLayout]) { }
    }

    internal class ScriptChat : AiOrchestrator
    {
        public ScriptChat(IDesignerChatHost host, Func<IChatClient> createChatClient, IScriptEditor editor)
            : base(new AiFunctionContext(host, createChatClient, editor), FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenScript]) { }
    }

    internal class CssChat : AiOrchestrator
    {
        public CssChat(IDesignerChatHost host, Func<IChatClient> createChatClient, ICssEditor editor)
            : base(new AiFunctionContext(host, createChatClient, editor), FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenCss]) { }
    }

    internal class PageFrameChat : AiOrchestrator
    {
        public PageFrameChat(IDesignerChatHost host, Func<IChatClient> createChatClient, IPageFrameEditor editor)
            : base(new AiFunctionContext(host, createChatClient, editor), FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenPageFrame]) { }
    }

    internal class QueryChat : AiOrchestrator
    {
        public QueryChat(IDesignerChatHost host, Func<IChatClient> createChatClient, IQueryEditor editor)
            : base(new AiFunctionContext(host, createChatClient, editor), FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenSql]) { }
    }

    internal class ExecuteSqlChat : AiOrchestrator
    {
        public ExecuteSqlChat(IDesignerChatHost host, Func<IChatClient> createChatClient, IExecuteSqlEditor editor)
            : base(new AiFunctionContext(host, createChatClient, editor), FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenSql]) { }
    }
}

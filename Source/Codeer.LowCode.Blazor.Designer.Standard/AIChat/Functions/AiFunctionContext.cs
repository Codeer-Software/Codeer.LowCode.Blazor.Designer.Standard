using Microsoft.Extensions.AI;
using Codeer.LowCode.Blazor.Designer.Extensibility;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // 機能が動作するために必要な環境。画面(コントロール)から渡される。
    // Editor は画面コントロールそのもの(this)で、その画面の面インターフェイス(IExecuteSqlEditor /
    // IModuleDetailLayoutEditor / … のいずれか1つ)を実装している。
    // ファクトリ(AiFunctionFactory)はこの Editor を面インターフェイスへ判定し、横断能力(フィールド/スクリプト)は
    // ブリッジクラスで AI 層の能力インターフェイス(IFieldContext / IScriptContext)へ適合させてから機能へ渡す。
    internal sealed class AiFunctionContext
    {
        public IDesignerChatHost Host { get; }
        public Func<IChatClient> CreateChatClient { get; }
        public object Editor { get; }

        public AiFunctionContext(IDesignerChatHost host, Func<IChatClient> createChatClient, object editor)
        {
            Host = host;
            CreateChatClient = createChatClient;
            Editor = editor;
        }
    }
}

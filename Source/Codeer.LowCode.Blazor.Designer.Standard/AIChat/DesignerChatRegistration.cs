using Microsoft.Extensions.AI;
namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat
{
    /// <summary>
    /// 各デザイナ画面(クエリ / SQL / 全体設定 / Detail・Search・List レイアウト / CSS / スクリプト / ページフレーム)へ
    /// AI チャットを一括で結線するヘルパ。全チャットは <see cref="IDesignerChatHost"/>(テスト可能なホスト)経由で動く。
    ///
    /// ホスト(本番は DesignerEnvironmentChatHost)は WPF の View に依存するため App 側で生成して渡す。
    /// DesignerEnvironment はホストから取得する(チャット本体には env を持たない IDesignerChatHost だけを渡す)。
    /// 本メソッドは「9 画面ぶんの Create*Chat デリゲート設定」という繰り返し部分だけを引き受ける。
    /// </summary>
    public static class DesignerChatRegistration
    {
        public static void RegisterScreenChats(IDesignerEnvironmentChatHost host, Func<IChatClient> createChatClient)
        {
            var env = host.DesignerEnvironment;
            env.CreateQueryChat = editor => new QueryChat(host, createChatClient, editor);
            env.CreateExecuteSqlChat = editor => new ExecuteSqlChat(host, createChatClient, editor);
            env.CreateOverallSettingsChat = editor => new OverallSettingsChat(host, createChatClient, editor);
            env.CreateDetailLayoutChat = editor => new DetailLayoutChat(host, createChatClient, editor);
            env.CreateSearchLayoutChat = editor => new SearchLayoutChat(host, createChatClient, editor);
            env.CreateListLayoutChat = editor => new ListLayoutChat(host, createChatClient, editor);
            env.CreateCssEditorChat = editor => new CssChat(host, createChatClient, editor);
            env.CreateScriptEditorChat = editor => new ScriptChat(host, createChatClient, editor);
            env.CreatePageFrameChat = editor => new PageFrameChat(host, createChatClient, editor);
        }
    }
}

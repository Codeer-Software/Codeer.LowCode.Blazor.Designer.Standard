using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat;
using Microsoft.Extensions.AI;

namespace Codeer.LowCode.Blazor.Designer.Standard
{
    /// <summary>DesignerStandard.Setup のオプション。</summary>
    public class DesignerStandardOptions
    {
        /// <summary>
        /// AI チャットが使う IChatClient のファクトリ。null なら AI チャットは登録しない。
        /// プロバイダ選択 (Azure OpenAI 等) と認証情報はアプリ側で持ち、ここに渡す。
        /// </summary>
        public Func<IChatClient>? CreateAiChatClient { get; set; }
    }

    /// <summary>
    /// デザイナ標準実装の一括セットアップ。
    /// DesignerApp.OnStartup の base.OnStartup(e) の後に呼ぶ (メニュー登録が MainWindow 生成後でないと効かないため)。
    ///
    /// 一部だけ使いたい場合はこのクラスを使わず、中身と同じコードを直接書けばよい:
    ///   StandardIcons.AddBootstrapIcons();
    ///   DesignerTemplateCandidate.Templates.Add(StandardTemplates.PatternShowcase());
    ///   StandardMenus.AddCreateDdl(DesignerEnvironment);
    ///   DesignerChatRegistration.RegisterScreenChats(new DesignerEnvironmentChatHost(DesignerEnvironment), factory);
    /// </summary>
    public static class DesignerStandard
    {
        public static void Setup(DesignerEnvironment env, DesignerStandardOptions? options = null)
        {
            StandardIcons.AddBootstrapIcons();
            StandardTemplates.AddAll();
            StandardMenus.AddAll(env);

            var createAiChatClient = options?.CreateAiChatClient;
            if (createAiChatClient != null)
            {
                DesignerChatRegistration.RegisterScreenChats(new DesignerEnvironmentChatHost(env), createAiChatClient);
            }
        }
    }
}

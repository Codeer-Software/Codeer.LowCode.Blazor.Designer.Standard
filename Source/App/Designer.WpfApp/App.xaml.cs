using Azure.AI.OpenAI;
using Codeer.LowCode.Bindings.ApexCharts.Designer;
using Codeer.LowCode.Blazor.Components.AppParts.Loading;
using Codeer.LowCode.Blazor.Designer;
using Codeer.LowCode.Blazor.Designer.Standard;
using Codeer.LowCode.Blazor.Extras.Designer;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.ClientModel;
using System.Configuration;
using System.Net.Http;
using System.Windows;
using WebApp.Client.Shared.AITextAnalyzer;
using WebApp.Client.Shared.ScriptObjects;

namespace Designer.WpfApp;

public partial class App : DesignerApp
{
    //AZURE_OPENAI_* の3つが揃っているときだけ Azure OpenAI の IChatClient ファクトリを返す(欠けていればAIチャット無効)。
    static Func<IChatClient>? CreateAzureOpenAIChatClientFactory()
    {
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_ENDPOINT");
        var key = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
        var model = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_MODEL");
        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(model)) return null;

        return () => new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(key))
            .GetChatClient(model)
            .AsIChatClient();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        ApexChartsDesignerInitializer.Initialize(BlazorRuntime);
        ExtrasDesignerInitializer.Initialize(BlazorRuntime);

        Codeer.LowCode.Blazor.License.LicenseManager.IsAutoUpdate =
            bool.TryParse(ConfigurationManager.AppSettings["IsLicenseAutoUpdate"], out var val) ? val : true;

        //アプリ固有: WebApp.Client.Shared / Server.Shared のサービス・スクリプト型を登録
        Services.AddSingleton<IDbAccessorFactory, DbAccessorFactory>();
        Services.AddSingleton<IAITextAnalyzerCore, AITextAnalyzerCoreDummy>();
        ScriptRuntimeTypeManager.AddType(typeof(ExcelCellIndex));
        ScriptRuntimeTypeManager.AddType(typeof(WebApp.Client.Shared.ScriptObjects.Excel));
        ScriptRuntimeTypeManager.AddService(new Toaster(null!));
        ScriptRuntimeTypeManager.AddService(new WebApiService(null!, null!));
        ScriptRuntimeTypeManager.AddType<WebApiResult>();
        ScriptRuntimeTypeManager.AddService(new MailService());
        ScriptRuntimeTypeManager.AddService(new LoadingService());
        ScriptRuntimeTypeManager.AddType<LoadingService.LoadingScope>();

        BlazorRuntime.InstallBundleCss("WebApp.Client.Shared");

        base.OnStartup(e);

        MainWindow.Title = "Designer";

        //標準実装一式 (アイコン候補 / プロジェクトテンプレート / ツールメニュー / AIチャット) を登録。
        //一部だけ使いたい場合は DesignerStandard.Setup の中身と同じコードを個別に書ける
        //(StandardTemplates / StandardMenus / StandardIcons / DesignerChatRegistration)。
        //AIチャットのモデルはライブラリが IChatClient 抽象しか知らないため、
        //プロバイダ選択(Azure OpenAI)と認証情報はアプリ側のここで持ち、ファクトリとして渡す。
        DesignerStandard.Setup(DesignerEnvironment, new DesignerStandardOptions
        {
            CreateAiChatClient = CreateAzureOpenAIChatClientFactory(),
        });

        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"An unhandled exception occurred:  {e.Exception.Message}{Environment.NewLine}{e.Exception.StackTrace}",
            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    class AITextAnalyzerCoreDummy : IAITextAnalyzerCore
    {
        public Task<ModuleData?> FileToModuleDataAsync(string moduleName, string fieldName, string fileName, StreamContent content)
            => throw new NotImplementedException();

        public Task<ModuleData?> TextToModuleDataAsync(string moduleName, string fieldName, string text)
            => throw new NotImplementedException();
    }
}

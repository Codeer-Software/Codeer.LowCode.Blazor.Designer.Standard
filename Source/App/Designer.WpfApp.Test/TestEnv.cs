using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using NUnit.Framework;
using System.ClientModel;

namespace Designer.WpfApp.Test
{
    public static class TestEnv
    {
        /// <summary>環境変数から実AI(Azure OpenAI)のIChatClientファクトリを作る。未設定なら Ignore。</summary>
        public static Func<IChatClient> RequireChatClientFactory()
        {
            var ep = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_ENDPOINT");
            var key = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
            var model = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_MODEL");
            if (string.IsNullOrEmpty(ep) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(model))
                Assert.Ignore("AZURE_OPENAI_API_ENDPOINT / _KEY / _MODEL が未設定のためスキップ");
            return () => new AzureOpenAIClient(new Uri(ep!), new ApiKeyCredential(key!))
                .GetChatClient(model!)
                .AsIChatClient();
        }
    }
}

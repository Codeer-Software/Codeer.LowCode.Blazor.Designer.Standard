using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// 拡張ライブラリ (Extras) の新フィールドを AIChat が正しく選べるかの実AIテスト。
    /// テストプロセスに Extras.Designer の最新 (0.4.0) を取り込み、そのフィールドドキュメントを
    /// FieldCatalog に登録したうえで、業務要件から Extras フィールドが選ばれるかを検証する。
    /// </summary>
    [TestFixture]
    public class ExtrasFieldChatTest
    {
        [OneTimeSetUp]
        public void RegisterExtrasDocs()
        {
            // Extras の型ロード + フィールドドキュメントを FieldCatalog へ登録する
            // (実アプリの App.xaml.cs と同じ登録。CSS は不要なので引数なし版でよい)。
#pragma warning disable CS0618
            Codeer.LowCode.Blazor.Extras.Designer.ExtrasDesignerInitializer.Initialize();
#pragma warning restore CS0618
        }

        static (FakeChatHost host, ModuleDesign module) BuildProductProject()
        {
            var module = new ModuleDesign { Name = "Product", DataSourceName = "Main", DbTable = "product" };
            module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            module.Fields.Add(new TextFieldDesign { Name = "Code", DbColumn = "code", DisplayName = "商品コード" });
            var designData = new DesignData { Modules = new FakeModuleDesigns(new[] { module }) };
            var dataSources = new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } };
            return (new FakeChatHost(designData, dataSources), module);
        }

        [Test]
        public async Task QRコード表示はQrCodeFieldで作る()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (host, module) = BuildProductProject();
            var chat = new FieldCreateFunction(host, settings, AiFunctionFactory.ToFieldContext(new FakeOverallSettingsEditor(module))!);

            var reply = await chat.ProcessMessage(
                "商品コード(Code)の値を QRコード として画面に表示する項目 CodeQr を追加してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(module));

            var qr = module.Fields.FirstOrDefault(f => f.Name == "CodeQr");
            Assert.That(qr, Is.Not.Null, "CodeQr が追加されていない");
            Assert.That(qr!.GetType().Name, Is.EqualTo("QrCodeFieldDesign"),
                $"QRコード表示なのに QrCodeField が使われていない (型={qr.GetType().Name})");
        }

        [Test]
        public async Task 一覧のCSV一括入出力はCsvFileFormatFieldで設定する()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (host, module) = BuildProductProject();
            var chat = new FieldCreateFunction(host, settings, AiFunctionFactory.ToFieldContext(new FakeOverallSettingsEditor(module))!);

            var reply = await chat.ProcessMessage(
                "この商品モジュールの一覧ページの一括ダウンロード/一括アップロードを、Excel ではなく CSV 形式にしたいです。" +
                "そのための設定用フィールドを追加してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(module));

            Assert.That(module.Fields.Any(f => f.GetType().Name == "CsvFileFormatFieldDesign"), Is.True,
                "CSV 形式の一括入出力なのに CsvFileFormatField が追加されていない " +
                $"(追加された型: {string.Join(",", module.Fields.Select(f => f.GetType().Name))})");
        }
    }
}

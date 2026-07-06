using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// 既存フィールドのプロパティを AI が正しく「編集」できるかのテスト。
    /// 表示名変更 / 最大長 / 小数桁 / 必須 / 候補追加 / リネーム を一度に指示し、各変更が反映されるか検証する。
    /// </summary>
    [TestFixture]
    public class FieldEditTest
    {
        [Test]
        public async Task 既存フィールドのプロパティ編集とリネーム()
        {
            var settings = TestEnv.RequireChatClientFactory();

            var module = new ModuleDesign { Name = "Sample", DataSourceName = "Main", DbTable = "sample" };
            module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            module.Fields.Add(new TextFieldDesign { Name = "ProductCode", DbColumn = "product_code", DisplayName = "コード", MaxLength = null });
            module.Fields.Add(new NumberFieldDesign { Name = "Price", DbColumn = "price", MaxFractionDigits = 2 });
            module.Fields.Add(new SelectFieldDesign { Name = "Status", DbColumn = "status", Candidates = new() { "有効,1", "無効,0" } });

            var designData = new DesignData { Modules = new FakeModuleDesigns(new[] { module }) };
            var dataSources = new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } };
            var host = new FakeChatHost(designData, dataSources);
            var chat = new FieldEditFunction(host, settings, AiFunctionFactory.ToFieldContext(new FakeOverallSettingsEditor(module))!);

            var reply = await chat.ProcessMessage(
                "既存フィールドを次のように編集してください: " +
                "(1) ProductCode の表示名を「商品コード」に、最大長を 20 に設定。" +
                "(2) Price の小数桁を 0 にする。" +
                "(3) Status の選択肢に「廃止,9」を追加する(既存の有効/無効は残す)。" +
                "(4) ProductCode フィールドの名前を ItemCode にリネームする。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(module));

            // (4) リネーム: ProductCode は消え ItemCode になっている
            Assert.That(module.Fields.Any(f => f.Name == "ProductCode"), Is.False, "リネーム前の名前 ProductCode が残っている");
            var code = module.Fields.OfType<TextFieldDesign>().FirstOrDefault(f => f.Name == "ItemCode");
            Assert.That(code, Is.Not.Null, "ItemCode にリネームされていない");
            // (1) 表示名・最大長
            Assert.That(code!.DisplayName, Is.EqualTo("商品コード"), "表示名が変更されていない");
            Assert.That(code.MaxLength, Is.EqualTo(20), "最大長が設定されていない");
            // (2) 小数桁
            var price = module.Fields.OfType<NumberFieldDesign>().Single(f => f.Name == "Price");
            Assert.That(price.MaxFractionDigits, Is.EqualTo(0), "小数桁が0に変更されていない");
            // (3) 候補追加(既存維持)
            var status = module.Fields.OfType<SelectFieldDesign>().Single(f => f.Name == "Status");
            Assert.That(status.Candidates.Count, Is.EqualTo(3), "候補が3件になっていない");
            Assert.That(status.Candidates.Any(c => c.Contains("廃止")), Is.True, "「廃止」候補が追加されていない");
            Assert.That(status.Candidates.Any(c => c.Contains("有効")), Is.True, "既存候補(有効)が消えている");
        }
    }
}

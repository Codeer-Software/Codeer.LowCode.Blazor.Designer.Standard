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
    /// 型は合っていても「設定値が正しいか」を検証する。型固有プロパティ(最大長/小数桁/候補の並び/参照先/ラジオ紐付け)を
    /// 指示どおりに設定できるか。ここで落ちる＝ドキュメント/プロンプトの型固有プロパティ説明が足りていない。
    /// </summary>
    [TestFixture]
    public class FieldPropertyCorrectnessTest
    {
        [Test]
        public async Task 型固有プロパティを指示どおりに設定する()
        {
            var settings = TestEnv.RequireChatClientFactory();

            var module = new ModuleDesign { Name = "Sample", DataSourceName = "Main", DbTable = "sample" };
            module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            var category = new ModuleDesign { Name = "Category", DataSourceName = "Main", DbTable = "category" };
            category.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            category.Fields.Add(new TextFieldDesign { Name = "CategoryName", DbColumn = "category_name" });

            var designData = new DesignData { Modules = new FakeModuleDesigns(new[] { module, category }) };
            var dataSources = new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } };
            var host = new FakeChatHost(designData, dataSources);
            var chat = new FieldCreateFunction(host, settings, AiFunctionFactory.ToFieldContext(new FakeOverallSettingsEditor(module))!);

            var reply = await chat.ProcessMessage(
                "次のフィールドを作成してください(DBカラム名 snake_case も付ける): " +
                "(1) Remarks: 複数行テキスト(IsMultiline=true)で最大500文字。" +
                "(2) Amount: 数値、小数2桁。" +
                "(3) Status: SelectField。選択肢は 表示『有効』=値 1、表示『無効』=値 0。" +
                "(4) CategoryRef: LinkField で Category モジュールを参照する。" +
                "(5) Kind: RadioGroupField と、その選択肢のラジオボタン2つ(表示『大』=値 L、表示『小』=値 S)。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(module));

            // (1) Text: 複数行 + 最大長
            var remarks = module.Fields.OfType<TextFieldDesign>().Single();
            Assert.That(remarks.IsMultiline, Is.True, "Remarks が複数行(IsMultiline)になっていない");
            Assert.That(remarks.MaxLength, Is.EqualTo(500), "Remarks の最大長が500でない");

            // (2) Number: 小数桁
            var amount = module.Fields.OfType<NumberFieldDesign>().Single();
            Assert.That(amount.MaxFractionDigits, Is.EqualTo(2), "Amount の小数桁が2でない");

            // (3) Select: 候補は "表示,値" の順
            var status = module.Fields.OfType<SelectFieldDesign>().Single();
            Assert.That(status.Candidates, Does.Contain("有効,1"), "Status 候補が『表示,値』順(有効,1)になっていない");
            Assert.That(status.Candidates, Does.Contain("無効,0"), "Status 候補(無効,0)が無い");

            // (4) Link: 参照先モジュール
            var link = module.Fields.OfType<LinkFieldDesign>().Single();
            Assert.That(link.SearchCondition.ModuleName, Is.EqualTo("Category"), "Link の参照先モジュールが Category でない");

            // (5) RadioGroup + RadioButton の紐付け(GroupField == グループの Name)
            var group = module.Fields.OfType<RadioGroupFieldDesign>().Single();
            var buttons = module.Fields.OfType<RadioButtonFieldDesign>().ToList();
            Assert.That(buttons.Count, Is.GreaterThanOrEqualTo(2), "ラジオボタンが2つ無い");
            Assert.That(buttons.All(b => b.GroupField == group.Name), Is.True,
                "ラジオボタンの GroupField がラジオグループの Name を指していない");
        }
    }
}

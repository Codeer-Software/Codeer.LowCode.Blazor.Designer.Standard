using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>CSS / Script / PageFrame の各 AI チャットを実 AI で回す統合テスト(改善ループ用)。</summary>
    [TestFixture]
    public class OtherChatsTest
    {
        static DesignData SampleDesign()
        {
            var m = new ModuleDesign { Name = "Product", DataSourceName = "Main", DbTable = "product" };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            return new DesignData { Modules = new FakeModuleDesigns(new[] { m }) };
        }

        static FakeChatHost Host() => new(SampleDesign(),
            new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } });

        [Test]
        public async Task CSS_カードに角丸を追加()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var editor = new FakeCssEditor();
            var chat = new CssFunction(Host(), settings, editor);

            var reply = await chat.ProcessMessage(".card に border-radius: 8px の角丸を付けるCSSを追加して。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(editor.Css);

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "CSS が適用(Update)されていない");
            Assert.That(editor.Css, Does.Contain("border-radius"), "border-radius が含まれていない");
        }

        [Test]
        public async Task Script_メッセージ表示処理を生成()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var editor = new FakeScriptEditor();
            var chat = new ScriptFunction(Host(), settings, AiFunctionFactory.ToScriptContext(editor)!);

            var reply = await chat.ProcessMessage("MessageBox に「保存しました」と表示する処理を書いてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(editor.Script);

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "スクリプトが適用(Update)されていない");
            Assert.That(editor.Script, Does.Contain("保存しました"), "指示した文言がスクリプトに含まれていない");
        }

        [Test]
        public async Task Script_数量と単価から金額を自動計算()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var m = new ModuleDesign { Name = "OrderDetail", DataSourceName = "Main", DbTable = "order_detail" };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new NumberFieldDesign { Name = "Quantity", DbColumn = "quantity" });
            m.Fields.Add(new NumberFieldDesign { Name = "UnitPrice", DbColumn = "unit_price", MaxFractionDigits = 2 });
            m.Fields.Add(new NumberFieldDesign { Name = "Amount", DbColumn = "amount", MaxFractionDigits = 2 });
            var dd = new DesignData { Modules = new FakeModuleDesigns(new[] { m }) };
            var host = new FakeChatHost(dd, new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } });
            var editor = new FakeScriptEditor();
            var chat = new ScriptFunction(host, settings, AiFunctionFactory.ToScriptContext(editor)!);

            var reply = await chat.ProcessMessage(
                "OrderDetail モジュールで、数量(Quantity)か単価(UnitPrice)が変わったら 金額(Amount) = 数量 × 単価 を自動で再計算するスクリプトを書いてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(editor.Script);

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "スクリプトが適用されていない");
            Assert.That(editor.Script, Does.Contain("Quantity"), "Quantity を参照していない");
            Assert.That(editor.Script, Does.Contain("UnitPrice"), "UnitPrice を参照していない");
            Assert.That(editor.Script, Does.Contain("Amount"), "Amount を参照していない");
            Assert.That(editor.Script, Does.Contain("*"), "乗算(数量×単価)になっていない");
        }

        [Test]
        public async Task Script_既存スクリプトを保持して新しい処理を追加()
        {
            var settings = TestEnv.RequireChatClientFactory();
            // 既存の処理を持った状態から編集する(空からの生成でなく「保持したまま追加」できるか)。
            var editor = new FakeScriptEditor(
                "void SaveButton_OnClick()\r\n{\r\n    MessageBox.Show(\"既存の処理\");\r\n}");
            var chat = new ScriptFunction(Host(), settings, AiFunctionFactory.ToScriptContext(editor)!);

            var reply = await chat.ProcessMessage(
                "今ある処理はそのまま残して、別に MessageBox で「新しい処理」と表示する関数を追加してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(editor.Script);

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "スクリプトが適用(Update)されていない");
            Assert.That(editor.Script, Does.Contain("既存の処理"), "既存の処理が消えている(全置換されている)");
            Assert.That(editor.Script, Does.Contain("新しい処理"), "追加した処理が含まれていない");
        }

        [Test]
        public async Task Script_保存ボタンでSubmitする処理を生成()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var editor = new FakeScriptEditor();
            var chat = new ScriptFunction(Host(), settings, AiFunctionFactory.ToScriptContext(editor)!);

            var reply = await chat.ProcessMessage(
                "保存ボタン(SaveButton)を押したら Submit してデータを保存する処理を書いてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(editor.Script);

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "スクリプトが適用(Update)されていない");
            Assert.That(editor.Script, Does.Contain("Submit"), "Submit を呼んでいない");
        }

        [Test]
        public async Task PageFrame_アプリケーションルートに設定()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var pf = new PageFrameDesign();
            var editor = new FakePageFrameEditor(pf);
            var chat = new PageFrameFunction(Host(), settings, editor);

            var reply = await chat.ProcessMessage("このページフレームをアプリケーションのルート(IsApplicationRoot)に設定してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(pf));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "PageFrame が適用(Update)されていない");
            Assert.That(pf.IsApplicationRoot, Is.True, "IsApplicationRoot が true になっていない");
        }
    }
}

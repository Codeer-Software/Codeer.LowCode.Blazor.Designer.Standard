using Codeer.LowCode.Blazor.Designer;
using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// ScriptFunction が「この環境で使えるスクリプトオブジェクト」(ScriptObjectCatalog 由来の動的カタログ) を
    /// プロンプトへ注入することの回帰テスト。
    /// 静的な ScriptExtensions.md の埋め込みを廃止した代わりの仕組みなので、ここが壊れると
    /// AI が拡張サービス (Excel / WebApi / Toaster / Mail / 独自登録) を知らずに生成することになる。
    /// </summary>
    [TestFixture]
    public class ScriptCatalogTest
    {
        //カタログ検証用のダミースクリプトサービス。
        //実在しない固有名にすることで、実 AI テストでもモデルの一般知識では書けない
        //(=カタログ情報を使ったことが分かる) ようにする。
        public class PointCardProbeService
        {
            public int AddPoint(string memberId, int point) => point;

            [ScriptHide]
            public void HiddenMember() { }
        }

        static DesignData SampleDesign()
        {
            var m = new ModuleDesign { Name = "Product", DataSourceName = "Main", DbTable = "product" };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            return new DesignData { Modules = new FakeModuleDesigns(new[] { m }) };
        }

        static FakeChatHost Host() => new(SampleDesign(),
            new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } });

        [OneTimeSetUp]
        public void Setup()
        {
            //テストプロセスのグローバル ScriptRuntimeTypeManager へ登録する
            //(AddService は同名上書きなので繰り返し実行しても安全)。
            DesignerApp.ScriptRuntimeTypeManager.AddService(new PointCardProbeService());
            ScriptObjectCatalog.Add<PointCardProbeService>(
                "PROBE-DOC: 会員にポイントを加算するサービス。`PointCardProbeService.AddPoint(memberId, point)` を使う。");
        }

        [Test]
        public async Task Script_プロンプトに登録済みスクリプトオブジェクトのカタログが入る()
        {
            var chat = new ScriptedChatClient("{\"Script\":\"void Test_OnClick()\\r\\n{\\r\\n}\",\"Explanation\":\"x\"}");
            var editor = new FakeScriptEditor();
            var fn = new ScriptFunction(Host(), () => chat, AiFunctionFactory.ToScriptContext(editor)!);

            await fn.ProcessMessage("なにか処理を書いて");

            Assert.That(chat.Requests, Has.Count.EqualTo(1));
            var systemTexts = string.Join("\n", chat.Requests[0]
                .Where(m => m.Role == ChatRole.System).Select(m => m.Text));

            Assert.That(systemTexts, Does.Contain("この環境で使えるスクリプトオブジェクト"), "カタログセクションが注入されていない");
            Assert.That(systemTexts, Does.Contain("PointCardProbeService"), "登録したサービスがカタログに載っていない");
            Assert.That(systemTexts, Does.Contain("AddPoint("), "サービスのメンバーシグネチャが載っていない");
            Assert.That(systemTexts, Does.Not.Contain("HiddenMember"), "[ScriptHide] のメンバーがカタログに載っている");
            Assert.That(systemTexts, Does.Contain("PROBE-DOC"), "ScriptObjectCatalog.Add で登録したドキュメントが載っていない");

            //組み込みサービス (コア登録) も載っていること
            Assert.That(systemTexts, Does.Contain("MessageBox"), "組み込みサービスがカタログに載っていない");
        }

        [Test]
        public async Task Script_実AI_カタログの独自サービスを使って生成()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var editor = new FakeScriptEditor();
            var fn = new ScriptFunction(Host(), settings, AiFunctionFactory.ToScriptContext(editor)!);

            var reply = await fn.ProcessMessage(
                "ボタン (AddPointButton) を押したら、会員ID \"M001\" に 10 ポイント加算する処理を書いてください。ポイント加算に使えるサービスが登録されています。");
            TestContext.Out.WriteLine(reply);
            TestContext.Out.WriteLine(editor.Script);

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "スクリプトが適用(Update)されていない");
            Assert.That(editor.Script, Does.Contain("PointCardProbeService"), "カタログで知らせた独自サービスを使っていない");
            Assert.That(editor.Script, Does.Contain("AddPoint"), "AddPoint を呼んでいない");
        }
    }
}

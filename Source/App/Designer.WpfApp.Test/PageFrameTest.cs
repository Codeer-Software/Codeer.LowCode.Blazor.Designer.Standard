using System;
using System.Linq;
using System.Reflection;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>PageFrameChat を実 AI で回し、全設定(特に権限・優先度・デバイス出し分け・自動ズーム)が AI から設定できるか検証する。</summary>
    [TestFixture]
    public class PageFrameTest
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

        static (PageFrameFunction chat, PageFrameDesign pf, FakePageFrameEditor editor) NewChat()
        {
            var pf = new PageFrameDesign { Name = "Main" };
            var editor = new FakePageFrameEditor(pf);
            return (new PageFrameFunction(Host(), TestEnv.RequireChatClientFactory(), editor), pf, editor);
        }

        // 決定的検証(AI非依存): Name と廃止プロパティ以外の「全プロパティ」が ApplyResponse で反映されることを総当たりで保証する。
        // 列挙の書き漏れ(=設定が黙って消える)を将来も含めて防ぐ。
        [Test]
        public void 全プロパティがApplyで反映されNameは保持される()
        {
            var props = typeof(PageFrameDesign).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite
                    && p.Name != nameof(PageFrameDesign.Name)
                    && p.GetCustomAttribute<ObsoleteAttribute>() == null)
                .ToList();
            Assert.That(props, Is.Not.Empty);

            var src = new PageFrameDesign { Name = "Src" };
            var dst = new PageFrameDesign { Name = "Dst" };
            foreach (var p in props) p.SetValue(src, Sentinel(p.PropertyType));

            PageFrameFunction.CopyEditableSettings(src, dst);

            foreach (var p in props)
                Assert.That(p.GetValue(dst), Is.EqualTo(p.GetValue(src)), $"プロパティ {p.Name} が反映されていない");
            Assert.That(dst.Name, Is.EqualTo("Dst"), "Name(識別子)が上書きされた");
        }

        static object? Sentinel(Type t)
        {
            var u = Nullable.GetUnderlyingType(t) ?? t;
            if (u == typeof(bool)) return true;
            if (u == typeof(int)) return 7;
            if (u == typeof(double)) return 3.0;
            if (u == typeof(string)) return "X";
            if (u.IsEnum) { var vs = Enum.GetValues(u); return vs.GetValue(vs.Length - 1); }
            return Activator.CreateInstance(u); // 参照型(SideBar/Header/Thickness/ModuleMatchCondition/List/ModulePage)は新規インスタンス
        }

        // 権限(UserReadCondition): ロールでアクセス制御。以前は ApplyResponse が UserReadCondition をコピーしておらず設定不能だった。
        [Test]
        public async Task 権限_ロールでアクセス制御を設定する()
        {
            TestEnv.RequireChatClientFactory();
            var (chat, pf, editor) = NewChat();

            var reply = await chat.ProcessMessage(
                "このフレームは、ログインユーザーのロール(Role)が admin のときだけ開けるようにアクセス権限(UserReadCondition)を設定してください。");
            TestContext.WriteLine(reply);
            var json = JsonConverterEx.SerializeObject(pf.UserReadCondition);
            TestContext.WriteLine(json);

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない");
            Assert.That(pf.UserReadCondition?.Condition, Is.Not.Null, "UserReadCondition に条件が設定されていない");
            Assert.That(json, Does.Contain("Role"), "条件が Role を参照していない");
            Assert.That(json, Does.Contain("admin"), "条件が admin を比較していない");
            Assert.That(json, Does.Contain("StringValue"), "比較値が StringValue でラップされていない");
            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<PageFrameDesign>(JsonConverterEx.SerializeObject(pf)), "壊れている");
        }

        // 優先度 Priority(以前は ApplyResponse でコピーされず設定不能だった)。
        [Test]
        public async Task 優先度Priorityを設定する()
        {
            TestEnv.RequireChatClientFactory();
            var (chat, pf, editor) = NewChat();

            var reply = await chat.ProcessMessage("このフレームをアプリケーションルートにして、優先度(Priority)を5にしてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(pf));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない");
            Assert.That(pf.IsApplicationRoot, Is.True, "IsApplicationRoot が true でない");
            Assert.That(pf.Priority, Is.EqualTo(5), "Priority が 5 になっていない");
        }

        // デバイス出し分け TargetDevice / WidthFrom(以前はコピーされず設定不能だった)。
        [Test]
        public async Task デバイス出し分け_PC向け画面幅条件を設定する()
        {
            TestEnv.RequireChatClientFactory();
            var (chat, pf, editor) = NewChat();

            var reply = await chat.ProcessMessage("画面幅900px以上のPC向けのアプリケーションルートにしてください(PCのときだけ・幅900以上で採用)。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(pf));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない");
            Assert.That(pf.IsApplicationRoot, Is.True, "IsApplicationRoot が true でない");
            Assert.That(pf.TargetDevice, Is.EqualTo(DeviceTarget.PC), "TargetDevice が PC でない");
            Assert.That(pf.WidthFrom, Is.EqualTo(900), "WidthFrom が 900 でない");
        }

        // 自動ズーム AutoZoom / BaseWidth(以前はコピーされず設定不能だった)。
        [Test]
        public async Task 自動ズームをFitToWidthに設定する()
        {
            TestEnv.RequireChatClientFactory();
            var (chat, pf, editor) = NewChat();

            var reply = await chat.ProcessMessage("画面幅に合わせて自動ズーム(FitToWidth)し、基準幅(BaseWidth)を1200にしてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(pf));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない");
            Assert.That(pf.AutoZoom, Is.EqualTo(AutoZoomMode.FitToWidth), "AutoZoom が FitToWidth でない");
            Assert.That(pf.BaseWidth, Is.EqualTo(1200), "BaseWidth が 1200 でない");
        }

        // サイドバー表示。
        [Test]
        public async Task 左サイドバーを表示にする()
        {
            TestEnv.RequireChatClientFactory();
            var (chat, pf, editor) = NewChat();

            var reply = await chat.ProcessMessage("左サイドバーを表示してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(pf));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない");
            Assert.That(pf.Left.IsVisible, Is.True, "左サイドバーが表示になっていない");
        }

        // ナビゲーション: 左サイドバーにモジュールへのリンクを追加。
        [Test]
        public async Task 左サイドバーにモジュールリンクを追加する()
        {
            TestEnv.RequireChatClientFactory();
            var (chat, pf, editor) = NewChat();

            var reply = await chat.ProcessMessage("左サイドバーに Product モジュールの一覧ページへのリンクを「商品」という名前で追加してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(pf.Left));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない");
            Assert.That(pf.Left.Links.Any(l => l.Module == "Product"), Is.True, "Product へのリンクが追加されていない");
        }

        // トップページ(ルートに表示するモジュール)を設定。
        [Test]
        public async Task トップページにモジュールを設定する()
        {
            TestEnv.RequireChatClientFactory();
            var (chat, pf, editor) = NewChat();

            var reply = await chat.ProcessMessage("トップページ(TopPageModuleDesign)に Product モジュールを表示してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(pf.TopPageModuleDesign));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない");
            Assert.That(pf.TopPageModuleDesign?.Module, Is.EqualTo("Product"), "トップページに Product が設定されていない");
        }

        // 表示モード(ModulePageType): 一覧→詳細(ListToDetail)。
        [Test]
        public async Task 表示モードをListToDetailにする()
        {
            TestEnv.RequireChatClientFactory();
            var (chat, pf, editor) = NewChat();

            var reply = await chat.ProcessMessage("トップページに Product を、一覧から詳細へ遷移する表示モード(ListToDetail)で表示してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(pf.TopPageModuleDesign));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない");
            Assert.That(pf.TopPageModuleDesign?.Module, Is.EqualTo("Product"));
            Assert.That(pf.TopPageModuleDesign?.ModulePageType, Is.EqualTo(ModulePageType.ListToDetail), "表示モードが ListToDetail になっていない");
        }

        // 表示モード(Detail)＋表示時のレイアウト名(DetailPageDesign.LayoutName)。
        [Test]
        public async Task 表示モードDetailと詳細レイアウト名を指定する()
        {
            TestEnv.RequireChatClientFactory();
            var (chat, pf, editor) = NewChat();

            var reply = await chat.ProcessMessage("トップページに Product を詳細(Detail)モードで表示し、詳細レイアウトは「Compact」を使ってください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(pf.TopPageModuleDesign));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない");
            Assert.That(pf.TopPageModuleDesign?.ModulePageType, Is.EqualTo(ModulePageType.Detail), "表示モードが Detail でない");
            Assert.That(pf.TopPageModuleDesign?.DetailPageDesign?.LayoutName, Is.EqualTo("Compact"), "詳細レイアウト名が Compact でない");
        }

        // 一覧の表示レイアウト(ListPageDesign.SearchLayoutName)。
        [Test]
        public async Task 一覧の検索レイアウト名を指定する()
        {
            TestEnv.RequireChatClientFactory();
            var (chat, pf, editor) = NewChat();

            var reply = await chat.ProcessMessage("トップページに Product を一覧(List)で表示し、検索レイアウトは「絞り込み」を使ってください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(pf.TopPageModuleDesign));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない");
            Assert.That(pf.TopPageModuleDesign?.ListPageDesign?.SearchLayoutName, Is.EqualTo("絞り込み"), "検索レイアウト名が指定どおりでない");
        }

        // ヘッダーを表示し、ヘッダーにモジュールリンクを置く。
        [Test]
        public async Task ヘッダーを表示しリンクを置く()
        {
            TestEnv.RequireChatClientFactory();
            var (chat, pf, editor) = NewChat();

            var reply = await chat.ProcessMessage("ヘッダーを表示して、ヘッダーに Product 一覧へのリンクを追加してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(pf.Header));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない");
            Assert.That(pf.Header.IsVisible, Is.True, "ヘッダーが表示になっていない");
            Assert.That(pf.Header.Links.Any(l => l.Module == "Product"), Is.True, "ヘッダーに Product リンクが無い");
        }

        // ナビ(サイドバー/ヘッダー)に出さず、URLで開けるページとして追加(OtherPageModuleDesigns)。
        [Test]
        public async Task ナビに出さないページを追加する()
        {
            TestEnv.RequireChatClientFactory();
            var (chat, pf, editor) = NewChat();

            var reply = await chat.ProcessMessage("Product を、サイドバーやヘッダーには出さずに、URLからだけ開けるページとして追加してください(OtherPageModuleDesigns)。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(pf.OtherPageModuleDesigns));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない");
            Assert.That(pf.OtherPageModuleDesigns.Any(p => p.Module == "Product"), Is.True, "非ナビページに Product が追加されていない");
            // ナビ(サイドバー/ヘッダー)には出していない
            Assert.That(pf.Left.Links.Any(l => l.Module == "Product"), Is.False, "サイドバーに出してしまっている");
            Assert.That(pf.Header.Links.Any(l => l.Module == "Product"), Is.False, "ヘッダーに出してしまっている");
        }
    }
}

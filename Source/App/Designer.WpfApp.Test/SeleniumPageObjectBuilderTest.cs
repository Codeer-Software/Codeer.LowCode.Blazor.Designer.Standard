using Codeer.LowCode.Blazor.Designer.Standard.SeleniumPageObject;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;
using NUnit.Framework;
using System.IO;

namespace Designer.WpfApp.Test
{
    /// <summary>Export PageObject が Extras 等の外部フィールドライブラリのドライバ名前空間を using し、検索コントロール無しのフィールドに SearchDriver を出さないこと。</summary>
    [TestFixture]
    public class SeleniumPageObjectBuilderTest
    {
        string _dir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "po_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        static DesignData Design()
        {
            var schedule = new ModuleDesign { Name = "Schedule", DataSourceName = "Main", DbTable = "schedules" };
            schedule.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            schedule.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "title" });
            schedule.Fields.Add(new ColorPickerFieldDesign { Name = "Color", DbColumn = "color" });
            schedule.DetailLayouts[""] = new DetailLayoutDesign
            {
                Layout = new GridLayoutDesign
                {
                    Rows =
                    {
                        new GridRow { Columns = { new GridColumn { Layout = new FieldLayoutDesign { FieldName = "Title" } }, new GridColumn { Layout = new FieldLayoutDesign { FieldName = "Color" } } } }
                    }
                }
            };
            schedule.SearchLayouts[""] = new SearchLayoutDesign
            {
                Layout = new SearchGridLayoutDesign
                {
                    Rows =
                    {
                        new GridRow { Columns = { new GridColumn { Layout = new FieldLayoutDesign { FieldName = "Title" } }, new GridColumn { Layout = new FieldLayoutDesign { FieldName = "Color" } } } }
                    }
                }
            };

            var page = new ModuleDesign { Name = "CalendarPage", DataSourceName = "Main" };
            page.Fields.Add(new CalendarFieldDesign { Name = "Calendar", SearchCondition = { ModuleName = "Schedule" } });
            page.Fields.Add(new SubmitButtonFieldDesign { Name = "Submit" });
            page.DetailLayouts[""] = new DetailLayoutDesign
            {
                Layout = new GridLayoutDesign
                {
                    Rows =
                    {
                        new GridRow { Columns = { new GridColumn { Layout = new FieldLayoutDesign { FieldName = "Calendar" } } } },
                        new GridRow { Columns = { new GridColumn { Layout = new FieldLayoutDesign { FieldName = "Submit" } } } },
                    }
                }
            };

            return new DesignData { Modules = new FakeModuleDesigns(new[] { schedule, page }) };
        }

        [Test]
        public void 外部フィールドのドライバ名前空間をusingしSearchDriverの無いフィールドは検索レイアウトから除く()
        {
            new SeleniumPageObjectBuilder { TargetPath = _dir, Namespace = "PageObject" }.Build(Design());

            var calendar = File.ReadAllText(Path.Combine(_dir, "CalendarPageDetailLayout.cs"));
            Assert.That(calendar, Does.Contain("using Codeer.LowCode.Blazor.Extras.SeleniumDrivers;"));
            Assert.That(calendar, Does.Contain("using Codeer.LowCode.Blazor.SeleniumDrivers;"));
            Assert.That(calendar, Does.Contain("public CalendarFieldDriver Calendar =>"));
            Assert.That(calendar, Does.Contain("public SubmitButtonFieldDriver Submit =>"));

            var scheduleDetail = File.ReadAllText(Path.Combine(_dir, "ScheduleDetailLayout.cs"));
            Assert.That(scheduleDetail, Does.Contain("public ColorPickerFieldDriver Color =>"));

            var scheduleSearch = File.ReadAllText(Path.Combine(_dir, "ScheduleSearchLayout.cs"));
            Assert.That(scheduleSearch, Does.Contain("public TextFieldSearchDriver Title =>"));
            Assert.That(scheduleSearch, Does.Not.Contain("ColorPickerFieldSearchDriver"));
        }

        [Test]
        public void 本体だけのモジュールには余分なusingを出さない()
        {
            var m = new ModuleDesign { Name = "Plain", DataSourceName = "Main", DbTable = "plain" };
            m.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            m.DetailLayouts[""] = new DetailLayoutDesign { Layout = new GridLayoutDesign { Rows = { new GridRow { Columns = { new GridColumn { Layout = new FieldLayoutDesign { FieldName = "Name" } } } } } } };
            var data = new DesignData { Modules = new FakeModuleDesigns(new[] { m }) };

            new SeleniumPageObjectBuilder { TargetPath = _dir, Namespace = "PageObject" }.Build(data);
            var src = File.ReadAllText(Path.Combine(_dir, "PlainDetailLayout.cs"));
            Assert.That(src, Does.Not.Contain("Extras.SeleniumDrivers"));
        }

        [Test]
        public void 未登録の名前空間は規約で解決する()
        {
            Assert.That(SeleniumPageObjectBuilder.ResolveDriverNamespace(typeof(TextFieldDesign)), Is.EqualTo("Codeer.LowCode.Blazor.SeleniumDrivers"));
            Assert.That(SeleniumPageObjectBuilder.ResolveDriverNamespace(typeof(CalendarFieldDesign)), Is.EqualTo("Codeer.LowCode.Blazor.Extras.SeleniumDrivers"));
        }
    }
}

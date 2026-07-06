using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// SQL 系 AI チャット(QueryField / ExecuteSqlField)を実 AI で回す。
    /// 全スキーマ一括モードでは通常1ターンで SQL 適用まで到達する。モデルが確認を挟んだ場合に備えて
    /// 2ターン目(承認)のフォールバックも残している。決定的な挙動検証は SqlFunctionUnitTest 側。
    /// </summary>
    [TestFixture]
    public class SqlChatTest
    {
        static DesignData EmptyDesign()
            => new() { Modules = new FakeModuleDesigns(System.Array.Empty<ModuleDesign>()) };

        static DbTableDefinition SalesTable()
        {
            var t = new DbTableDefinition { Name = "sales" };
            t.Columns.Add(new DbColumnDefinition { Name = "id", RawDbTypeName = "INTEGER", NetTypeFullName = typeof(long).FullName!, IsNullable = false });
            t.Columns.Add(new DbColumnDefinition { Name = "product_id", RawDbTypeName = "INTEGER", NetTypeFullName = typeof(long).FullName!, IsNullable = false });
            t.Columns.Add(new DbColumnDefinition { Name = "amount", RawDbTypeName = "NUMERIC", NetTypeFullName = typeof(decimal).FullName!, IsNullable = false });
            t.Columns.Add(new DbColumnDefinition { Name = "sale_date", RawDbTypeName = "DATE", NetTypeFullName = typeof(System.DateTime).FullName!, IsNullable = true });
            return t;
        }

        static void AssertAggregateSql(string? sql)
        {
            Assert.That(sql, Is.Not.Null.And.Not.Empty, "SQL が生成・適用されていない");
            var u = sql!.ToUpperInvariant();
            Assert.That(u, Does.Contain("SALES"), "SQL に対象テーブル sales が含まれない");
            Assert.That(u, Does.Contain("SUM"), "集計(SUM)になっていない");
            Assert.That(u, Does.Contain("GROUP BY"), "GROUP BY が含まれない");
        }

        [Test]
        public async Task QueryField_集計SQLを生成して適用()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var host = new FakeChatHost(EmptyDesign(),
                new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } },
                _ => new List<DbTableDefinition> { SalesTable() });
            var editor = new FakeQueryEditor("Main");
            var chat = new QueryFunction(host, settings, editor);

            var r1 = await chat.ProcessMessage("sales テーブルから商品ごと(product_id)の売上(amount)合計を集計するSQLを作ってください。");
            TestContext.WriteLine("turn1:\n" + r1);
            if (editor.AppliedSql == null)
            {
                var r2 = await chat.ProcessMessage("はい、その sales テーブルでそのままSQLを作成してください。");
                TestContext.WriteLine("turn2:\n" + r2);
            }
            TestContext.WriteLine("applied SQL:\n" + editor.AppliedSql);
            AssertAggregateSql(editor.AppliedSql);
        }

        [Test]
        public async Task QueryField_スキーマに無いデータの要求では確認を求めてSQLを適用しない()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var host = new FakeChatHost(EmptyDesign(),
                new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } },
                _ => new List<DbTableDefinition> { SalesTable() });
            var editor = new FakeQueryEditor("Main");
            var chat = new QueryFunction(host, settings, editor);

            //sales には顧客も原価も無い。もっともらしい代用で作らず、確認を求めるべきケース
            var r1 = await chat.ProcessMessage("顧客ごとの粗利(売上-原価)を月別に集計するSQLを作ってください。");
            TestContext.WriteLine("turn1:\n" + r1);

            Assert.That(editor.AppliedSql, Is.Null, "実現不可能な要求に対して自信満々にSQLを適用してしまった");
        }

        [Test]
        public async Task ExecuteSqlField_集計SQLを生成して適用()
        {
            var settings = TestEnv.RequireChatClientFactory();
            using var db = new SqliteTestDb("sql_chat.db");
            db.ExecuteDdl("CREATE TABLE sales (id INTEGER PRIMARY KEY, product_id INTEGER, amount NUMERIC, sale_date DATE);");

            var ds = new DataSource { Name = "Main", DataSourceType = DataSourceType.SQLite, ConnectionString = db.ConnectionString };
            var host = new FakeChatHost(EmptyDesign(), new List<DataSource> { ds }, _ => db.GetSchema());
            var editor = new FakeExecuteSqlEditor(ds);
            var chat = new ExecuteSqlFunction(host, settings, editor);

            var r1 = await chat.ProcessMessage("sales テーブルから商品ごと(product_id)の売上(amount)合計を集計するSQLを作ってください。");
            TestContext.WriteLine("turn1:\n" + r1);
            if (editor.AppliedSql == null)
            {
                var r2 = await chat.ProcessMessage("はい、その sales テーブルでそのままSQLを作成してください。");
                TestContext.WriteLine("turn2:\n" + r2);
            }
            TestContext.WriteLine("applied SQL:\n" + editor.AppliedSql);
            AssertAggregateSql(editor.AppliedSql);
        }
    }
}

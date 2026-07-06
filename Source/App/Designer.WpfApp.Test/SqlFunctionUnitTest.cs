using System.IO;
using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// SQL 系機能(QueryFunction / ExecuteSqlFunction)の決定的テスト(実 AI を使わない)。
    /// スクリプト済み応答を返す IChatClient で、全スキーマ一括モード・巨大スキーマのフォールバック・
    /// 実行チェックの自動修正ループを検証する。
    /// </summary>
    [TestFixture]
    public class SqlFunctionUnitTest
    {
        static DesignData EmptyDesign()
            => new() { Modules = new FakeModuleDesigns(System.Array.Empty<ModuleDesign>()) };

        static DbTableDefinition SalesTable()
        {
            var t = new DbTableDefinition { Name = "sales" };
            t.Columns.Add(new DbColumnDefinition { Name = "id", RawDbTypeName = "INTEGER", NetTypeFullName = typeof(long).FullName!, IsNullable = false });
            t.Columns.Add(new DbColumnDefinition { Name = "product_id", RawDbTypeName = "INTEGER", NetTypeFullName = typeof(long).FullName!, IsNullable = false });
            t.Columns.Add(new DbColumnDefinition { Name = "amount", RawDbTypeName = "NUMERIC", NetTypeFullName = typeof(decimal).FullName!, IsNullable = false });
            return t;
        }

        static string SqlResponse(string sql)
            => $"できました。\n```sql\n{sql}\n```\n```schema\n[{{\"Name\":\"id\",\"DbType\":\"INTEGER\"}}]\n```";

        static string AllRequestText(List<ChatMessage> messages)
            => string.Join("\n----\n", messages.Select(m => m.Text));

        [Test]
        public async Task 全スキーマモード_1ターンで適用_スキーマとマッピングがプロンプトに入る()
        {
            var host = new FakeChatHost(EmptyDesign(),
                new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } },
                _ => new List<DbTableDefinition> { SalesTable() });

            var editor = new FakeQueryEditor("Main")
            {
                ModuleDesignOverride = new ModuleDesign
                {
                    Name = "SalesSummary",
                    Fields = { new NumberFieldDesign { Name = "Amount", DbColumn = "total_amount" } },
                },
            };
            var client = new ScriptedChatClient(SqlResponse("SELECT product_id, SUM(amount) AS total_amount FROM sales GROUP BY product_id"));
            var chat = new QueryFunction(host, () => client, editor);

            var result = await chat.ExecuteAsync("商品ごとの売上合計を集計して");

            Assert.That(editor.AppliedSql, Does.Contain("GROUP BY"), "1ターンでSQLが適用されていない");
            Assert.That(client.Requests, Has.Count.EqualTo(1), "AI呼び出しは1回のはず");

            var prompt = AllRequestText(client.Requests[0]);
            Assert.That(prompt, Does.Contain("amount:NUMERIC"), "全テーブルの列定義がプロンプトに入っていない");
            Assert.That(prompt, Does.Contain("total_amount"), "モジュールのDbColumnマッピング情報が入っていない");
            Assert.That(prompt, Does.Not.Contain("```tbl"), "全スキーマモードでテーブル選択プロトコルが残っている");
            Assert.That(prompt, Does.Contain("確度80%"), "自信過剰ゲート(自己チェック)がプロンプトに入っていない");
            Assert.That(result.Outcome, Is.EqualTo(FunctionOutcome.Done));
        }

        [Test]
        public async Task ヒントtxtあり_全読み込みせずヒント中心のテーブル選択フローになる()
        {
            var hintDir = Path.Combine(Path.GetTempPath(), "clb_sqlfn_test_" + TestContext.CurrentContext.Test.ID);
            Directory.CreateDirectory(hintDir);
            try
            {
                File.WriteAllText(Path.Combine(hintDir, "Main.txt"),
                    "このDBは巨大です。売上分析には sales テーブルだけを使ってください。他のテーブルは無視してよいです。");

                var host = new FakeChatHost(EmptyDesign(),
                    new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } },
                    _ => new List<DbTableDefinition> { SalesTable() })
                { CurrentFileDirectory = hintDir };

                var editor = new FakeQueryEditor("Main");
                var client = new ScriptedChatClient(
                    "```tbl\nsales\n```",
                    SqlResponse("SELECT product_id, SUM(amount) AS amount FROM sales GROUP BY product_id"));
                var chat = new QueryFunction(host, () => client, editor);

                var result = await chat.ExecuteAsync("商品ごとの売上合計を集計して");

                var firstPrompt = AllRequestText(client.Requests[0]);
                Assert.That(firstPrompt, Does.Contain("使うテーブルを決めます"), "ヒントtxtありはテーブル選択フローになるはず");
                Assert.That(firstPrompt, Does.Contain("確度80%"), "自信過剰ゲート(自己チェック)が選択フローのプロンプトに入っていない");
                Assert.That(firstPrompt, Does.Contain("巨大です"), "ヒントtxtの内容がプロンプトに入っていない");
                Assert.That(firstPrompt, Does.Not.Contain("amount:NUMERIC"), "ヒントtxtありなのに全スキーマが読み込まれている");

                //選択後は選択テーブルの列詳細が提供される
                Assert.That(AllRequestText(client.Requests[1]), Does.Contain("amount:NUMERIC"), "選択テーブルの列詳細が提供されていない");
                Assert.That(editor.AppliedSql, Does.Contain("GROUP BY"), "選択フロー経由でSQLが適用されていない");
                Assert.That(result.Outcome, Is.EqualTo(FunctionOutcome.Done));
            }
            finally
            {
                try { Directory.Delete(hintDir, true); } catch { }
            }
        }

        [Test]
        public async Task 巨大スキーマ_テーブル選択フェーズにフォールバックする()
        {
            //FullSchemaMaxLength を確実に超える定義を作る
            var tables = new List<DbTableDefinition>();
            for (var i = 0; i < 3000; i++)
            {
                var t = new DbTableDefinition { Name = $"table_{i:0000}" };
                t.Columns.Add(new DbColumnDefinition { Name = "some_column_name_long", RawDbTypeName = "VARCHAR(100)", IsNullable = true });
                t.Columns.Add(new DbColumnDefinition { Name = "another_column_name", RawDbTypeName = "INTEGER", IsNullable = true });
                tables.Add(t);
            }
            tables.Add(SalesTable());

            var host = new FakeChatHost(EmptyDesign(),
                new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } },
                _ => tables);
            var editor = new FakeQueryEditor("Main");
            var client = new ScriptedChatClient(
                "```tbl\nsales\n```",
                SqlResponse("SELECT product_id, SUM(amount) AS amount FROM sales GROUP BY product_id"));
            var chat = new QueryFunction(host, () => client, editor);

            var result = await chat.ExecuteAsync("商品ごとの売上合計を集計して");

            Assert.That(AllRequestText(client.Requests[0]), Does.Contain("使うテーブルを決めます"), "フォールバック時はテーブル選択フェーズになるはず");
            Assert.That(editor.AppliedSql, Does.Contain("GROUP BY"), "選択フェーズ経由でSQLが適用されていない");
            Assert.That(result.Outcome, Is.EqualTo(FunctionOutcome.Done));
        }

        [Test]
        public async Task 選択フロー_後からテーブルを追加選択できる_ヒントは残る()
        {
            var hintDir = Path.Combine(Path.GetTempPath(), "clb_sqlfn_test_" + TestContext.CurrentContext.Test.ID);
            Directory.CreateDirectory(hintDir);
            try
            {
                File.WriteAllText(Path.Combine(hintDir, "Main.txt"), "売上は sales、商品マスタは products。");

                var products = new DbTableDefinition { Name = "products" };
                products.Columns.Add(new DbColumnDefinition { Name = "id", RawDbTypeName = "INTEGER", IsNullable = false });
                products.Columns.Add(new DbColumnDefinition { Name = "product_name", RawDbTypeName = "TEXT", IsNullable = true });

                var host = new FakeChatHost(EmptyDesign(),
                    new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } },
                    _ => new List<DbTableDefinition> { SalesTable(), products })
                { CurrentFileDirectory = hintDir };

                var editor = new FakeQueryEditor("Main");
                var client = new ScriptedChatClient(
                    "```tbl\nsales\n```",
                    SqlResponse("SELECT product_id, SUM(amount) AS amount FROM sales GROUP BY product_id"),
                    "```tbl\nsales, products\n```",
                    SqlResponse("SELECT p.product_name, SUM(s.amount) AS amount FROM sales s JOIN products p ON p.id = s.product_id GROUP BY p.product_name"));
                var chat = new QueryFunction(host, () => client, editor);

                await chat.ExecuteAsync("商品ごとの売上合計を集計して");
                Assert.That(editor.AppliedSql, Does.Not.Contain("JOIN"));

                await chat.ExecuteAsync("products をJOINして商品名で出して");
                Assert.That(editor.AppliedSql, Does.Contain("JOIN products"), "追加選択後のSQLが適用されていない");

                //追加選択後のリクエストに products の列定義が提供されている
                var lastPrompt = AllRequestText(client.Requests[3]);
                Assert.That(lastPrompt, Does.Contain("product_name:TEXT"), "追加テーブルの列定義が提供されていない");
                //人手のヒントは選択後も残る(業務情報のため)
                Assert.That(lastPrompt, Does.Contain("商品マスタは products"), "ヒントtxtが選択後に消えている");
            }
            finally
            {
                try { Directory.Delete(hintDir, true); } catch { }
            }
        }

        [Test]
        public async Task 実行チェック_エラーSQLはAIに差し戻して自動修正される()
        {
            using var db = new SqliteTestDb("sql_fn_validate.db");
            db.ExecuteDdl("CREATE TABLE sales (id INTEGER PRIMARY KEY, product_id INTEGER, amount NUMERIC);");
            var ds = new DataSource { Name = "Main", DataSourceType = DataSourceType.SQLite, ConnectionString = db.ConnectionString };

            var host = new FakeChatHost(EmptyDesign(), new List<DataSource> { ds }, _ => db.GetSchema());
            var editor = new FakeQueryEditor("Main", "", ds);
            var client = new ScriptedChatClient(
                SqlResponse("SELECT no_such_column FROM sales"),   //1回目: 存在しない列
                SqlResponse("SELECT id, amount FROM sales"));      //2回目: 修正版
            var chat = new QueryFunction(host, () => client, editor);

            var result = await chat.ExecuteAsync("売上を一覧にして");

            Assert.That(client.Requests, Has.Count.EqualTo(2), "検証エラーでAIに差し戻されていない");
            Assert.That(AllRequestText(client.Requests[1]), Does.Contain("エラー"), "差し戻しメッセージにエラー内容が入っていない");
            Assert.That(editor.AppliedSql, Does.Contain("id, amount"), "修正版SQLが適用されていない");
            Assert.That(result.Message, Does.Contain("実行チェックOK"));
        }

        [Test]
        public async Task 実行チェック_システムページングパラメータをバインドして通る()
        {
            using var db = new SqliteTestDb("sql_fn_paging.db");
            db.ExecuteDdl("CREATE TABLE sales (id INTEGER PRIMARY KEY, amount NUMERIC);");
            var ds = new DataSource { Name = "Main", DataSourceType = DataSourceType.SQLite, ConnectionString = db.ConnectionString };

            var host = new FakeChatHost(EmptyDesign(), new List<DataSource> { ds }, _ => db.GetSchema());
            var editor = new FakeQueryEditor("Main", "", ds);
            var client = new ScriptedChatClient(
                SqlResponse("SELECT id, amount FROM sales ORDER BY id LIMIT @rows_per_page OFFSET @offset"));
            var chat = new QueryFunction(host, () => client, editor);

            var result = await chat.ExecuteAsync("売上をページングつきで一覧にして");

            if (client.Requests.Count > 1) TestContext.WriteLine("retry request:\n" + AllRequestText(client.Requests[1]));
            Assert.That(client.Requests, Has.Count.EqualTo(1), "ページングパラメータのバインド漏れで差し戻しが発生している");
            Assert.That(result.Message, Does.Contain("実行チェックOK"));
        }

        [Test]
        public void バインドパラメータ収集_宣言分とSQL中のシステムパラメータを拾う()
        {
            var sql = "SELECT * FROM sales WHERE amount >= @min OR @min IS NULL LIMIT @rows_per_page OFFSET @offset";
            var declared = new List<DbParameterSetting>
            {
                new() { IsParameter = true, Name = "@min", DbType = "NUMERIC" },
                new() { IsParameter = false, Name = "amount", DbType = "NUMERIC" }, //出力列は対象外
            };

            var names = SqlFunctionBase.CollectBindParameterNames(sql, declared);

            Assert.That(names, Is.EquivalentTo(new[] { "@min", "@rows_per_page", "@offset" }));
        }

        [Test]
        public async Task 検証環境なし_チェックはスキップして従来どおり適用する()
        {
            var host = new FakeChatHost(EmptyDesign(),
                new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } },
                _ => new List<DbTableDefinition> { SalesTable() });
            var editor = new FakeQueryEditor("Main"); //DataSource なし → CreateDbAccessor が失敗 → スキップ
            var client = new ScriptedChatClient(SqlResponse("SELECT id FROM sales"));
            var chat = new QueryFunction(host, () => client, editor);

            var result = await chat.ExecuteAsync("id一覧");

            Assert.That(editor.AppliedSql, Does.Contain("SELECT id"));
            Assert.That(result.Message, Does.Not.Contain("実行チェックOK"), "検証していないのにOK表記になっている");
        }
    }
}

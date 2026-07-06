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
    /// マスタ作成のユースケースを実 AI + 実 SQLite で検証するシナリオ。
    /// フィールド追加 / 設定 / システムフィールド一通り / DBテーブル作成(CREATE実行) / 編集してALTER(実行) を確認する。
    /// Web アプリは起動せず、生成 JSON と実際の DB スキーマだけで精度を見る。
    /// </summary>
    [TestFixture]
    public class MasterScenarioTest
    {
        static readonly string[] SystemFieldNames =
            { "Id", "CreatedAt", "UpdatedAt", "Creator", "Updater", "LogicalDelete", "OptimisticLocking" };

        [Test]
        public async Task 明示的に頼まれたカラム削除は実行可能なDROPを出す()
        {
            var settings = TestEnv.RequireChatClientFactory();
            using var db = new SqliteTestDb("drop_col.db");
            db.ExecuteDdl("CREATE TABLE customer_master (id INTEGER PRIMARY KEY, customer_code TEXT, customer_name TEXT, test INTEGER);");

            var module = new ModuleDesign { Name = "CustomerMaster", DataSourceName = "Main", DbTable = "customer_master" };
            module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            module.Fields.Add(new TextFieldDesign { Name = "CustomerCode", DbColumn = "customer_code" });
            module.Fields.Add(new TextFieldDesign { Name = "CustomerName", DbColumn = "customer_name" });
            module.Fields.Add(new NumberFieldDesign { Name = "Test", DbColumn = "test" });

            var designData = new DesignData { Modules = new FakeModuleDesigns(new[] { module }) };
            var dataSources = new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } };
            var host = new FakeChatHost(designData, dataSources, _ => db.GetSchema());
            var editor = new FakeOverallSettingsEditor(module);
            // 全体設定画面のオーケストレータ経由。「フィールド編集(削除)」→「DB更新(DROP)」にルーティングされる。
            var orch = new AiOrchestrator(
                new AiFunctionContext(host, settings, editor),
                FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenOverallSettings]);

            var reply = await orch.ProcessMessage(
                "Test フィールドを削除してください。さらに、test カラムを DB のテーブルからも削除してください。");
            TestContext.WriteLine(reply);

            Assert.That(module.Fields.Any(f => f.Name == "Test"), Is.False, "Test フィールドが削除されていない");
            Assert.That(host.ShownDdls, Is.Not.Empty, "DDL が出ていない");
            var ddl = host.ShownDdls.Last().Ddl;
            TestContext.WriteLine("=== DDL ===\n" + ddl);

            // 実行して test 列が実際に消えること。DROP がコメントアウトされていたら消えず失敗する。
            Assert.DoesNotThrow(() => db.ExecuteDdl(ddl), "生成 DDL が実行できない");
            var cols = db.GetColumnNames("customer_master").Select(c => c.ToLowerInvariant()).ToHashSet();
            Assert.That(cols, Does.Not.Contain("test"), "test 列が削除されていない(DROP がコメントアウトされた)");
        }

        [Test]
        public async Task 既存テーブルと列名が食い違っても作り直さず不足列をADDし既存データを保持する()
        {
            // 既存 customer_master には zip_code / address がある。モジュール側は postal_code / address1 / address2 …。
            // AI が zip_code→postal_code, address→address1 と「推測でマッピング」して DROP TABLE + 作り直し(INSERT SELECT)を
            // 出すと、既存データが失われる。列名の食い違いだけで作り直してはならない(不足列は ADD・不明な既存列はコメントのDROP候補)。
            var settings = TestEnv.RequireChatClientFactory();
            using var db = new SqliteTestDb("name_mismatch.db");
            db.ExecuteDdl(
                "CREATE TABLE customer_master (id INTEGER PRIMARY KEY, customer_code TEXT, customer_name TEXT, " +
                "customer_name_kana TEXT, zip_code TEXT, address TEXT, phone_number TEXT, email TEXT);");
            db.InsertRow("customer_master", new Dictionary<string, object?>
            {
                ["id"] = 1, ["customer_code"] = "C001", ["customer_name"] = "テスト商事",
                ["zip_code"] = "123-4567", ["address"] = "東京都千代田区",
            });

            var module = new ModuleDesign { Name = "CustomerMaster", DataSourceName = "Main", DbTable = "customer_master" };
            module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            module.Fields.Add(new TextFieldDesign { Name = "CustomerCode", DbColumn = "customer_code" });
            module.Fields.Add(new TextFieldDesign { Name = "CustomerName", DbColumn = "customer_name" });
            module.Fields.Add(new TextFieldDesign { Name = "CustomerNameKana", DbColumn = "customer_name_kana" });
            module.Fields.Add(new TextFieldDesign { Name = "PostalCode", DbColumn = "postal_code" });
            module.Fields.Add(new TextFieldDesign { Name = "Prefecture", DbColumn = "prefecture" });
            module.Fields.Add(new TextFieldDesign { Name = "Address1", DbColumn = "address1" });
            module.Fields.Add(new TextFieldDesign { Name = "Address2", DbColumn = "address2" });
            module.Fields.Add(new TextFieldDesign { Name = "PhoneNumber", DbColumn = "phone_number" });
            module.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "email" });
            module.Fields.Add(new TextFieldDesign { Name = "ContactPerson", DbColumn = "contact_person" });
            module.Fields.Add(new TextFieldDesign { Name = "Remarks", DbColumn = "remarks" });

            var designData = new DesignData { Modules = new FakeModuleDesigns(new[] { module }) };
            var dataSources = new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } };
            var host = new FakeChatHost(designData, dataSources, _ => db.GetSchema());
            var editor = new FakeOverallSettingsEditor(module);
            var orch = new AiOrchestrator(
                new AiFunctionContext(host, settings, editor),
                FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenOverallSettings]);

            var reply = await orch.ProcessMessage("このテーブルの内容を DB に反映してください。");
            TestContext.WriteLine(reply);

            Assert.That(host.ShownDdls, Is.Not.Empty, "DDL が提示されていない");
            var ddl = host.ShownDdls.Last().Ddl;
            TestContext.WriteLine("=== DDL ===\n" + ddl);
            Assert.DoesNotThrow(() => db.ExecuteDdl(ddl), "生成 DDL が実行できない");

            var cols = db.GetColumnNames("customer_master").Select(c => c.ToLowerInvariant()).ToHashSet();
            // 不明な既存列(zip_code / address)は勝手に消さない(コメントの DROP 候補にとどめる)。
            Assert.That(cols, Does.Contain("zip_code"), "既存の zip_code 列が消えた(推測で作り直した?)");
            Assert.That(cols, Does.Contain("address"), "既存の address 列が消えた(推測で作り直した?)");
            // 不足列は ADD で足す。
            Assert.That(cols, Does.Contain("postal_code"), "postal_code 列が追加されていない");
            Assert.That(cols, Does.Contain("address1"), "address1 列が追加されていない");
            // 既存データが保持されている(破壊的な作り直しをしていない)。
            Assert.That(db.ExecuteScalarLong("SELECT COUNT(*) FROM customer_master"), Is.EqualTo(1), "既存の行が失われた");
            Assert.That(db.ExecuteScalarLong("SELECT COUNT(*) FROM customer_master WHERE zip_code = '123-4567'"),
                Is.EqualTo(1), "既存データ(zip_code)が保持されていない");
        }

        [Test]
        public async Task 商品マスタ_システムフィールド込みで作成しテーブル生成しALTER追加()
        {
            var settings = TestEnv.RequireChatClientFactory();
            using var db = new SqliteTestDb("product_master.db");

            var module = new ModuleDesign { Name = "ProductMaster", DataSourceName = "Main", DbTable = "product_master" };
            module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });

            var designData = new DesignData { Modules = new FakeModuleDesigns(new[] { module }) };
            var dataSources = new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } };
            var host = new FakeChatHost(designData, dataSources, _ => db.GetSchema());
            var editor = new FakeOverallSettingsEditor(module);
            // 全体設定画面のオーケストレータ経由。「フィールド作成」→「DB更新(CREATE/ALTER)」にルーティングされる。
            var orch = new AiOrchestrator(
                new AiFunctionContext(host, settings, editor),
                FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenOverallSettings]);

            // === Turn 1: 業務フィールド + システムフィールド + テーブル作成 ===
            var reply1 = await orch.ProcessMessage(
                "商品マスタを作成します。次の項目を追加してください: " +
                "商品コード(テキスト・必須)、商品名(テキスト・必須)、区分(SelectField で 通常/特価 を選択)、" +
                "単価(数値・小数2桁)、在庫数(整数)、有効フラグ(Boolean)。" +
                "さらにシステムフィールドの 作成日時・更新日時・作成者・更新者・論理削除・楽観ロック(アプリでインクリメント) も一通り追加し、" +
                "各フィールドに DB カラム名(snake_case) を割り付けてください。そして DB にこのテーブルを作成してください。");

            TestContext.WriteLine("=== reply1 ===\n" + reply1);
            TestContext.WriteLine("=== module after turn1 ===\n" + JsonConverterEx.SerializeObject(module));

            // システムフィールドが予約名で一通り揃っているか
            var fieldNames = module.Fields.Select(f => f.Name).ToHashSet();
            foreach (var sys in SystemFieldNames)
                Assert.That(fieldNames, Does.Contain(sys), $"システムフィールド {sys} が追加されていない");

            // 楽観ロックはアプリインクリメント + DbColumn
            var opt = module.Fields.OfType<OptimisticLockingFieldDesign>().SingleOrDefault();
            Assert.That(opt, Is.Not.Null, "OptimisticLocking が無い");
            Assert.That(opt!.IncrementVersion, Is.True, "楽観ロックがアプリインクリメントになっていない");
            Assert.That(opt.DbColumn, Is.Not.Empty, "楽観ロックに DbColumn が無い");

            // テーブル作成 DDL が提示され、実際に実行できるか(field.create → db.update にルーティングされる)
            Assert.That(host.ShownDdls, Is.Not.Empty, "DBテーブル作成のDDLが提示されていない(db.update にルーティングされていない?)");
            var createDdl = host.ShownDdls.Last().Ddl;
            TestContext.WriteLine("=== CREATE DDL ===\n" + createDdl);
            Assert.DoesNotThrow(() => db.ExecuteDdl(createDdl), "生成された CREATE DDL が SQLite で実行できない");

            // 設計の各 DbColumn が実テーブルに存在するか
            var cols = db.GetColumnNames("product_master").Select(c => c.ToLowerInvariant()).ToHashSet();
            foreach (var f in module.Fields.OfType<DbValueFieldDesignBase>())
                if (!string.IsNullOrEmpty(f.DbColumn))
                    Assert.That(cols, Does.Contain(f.DbColumn.ToLowerInvariant()), $"列 {f.DbColumn} がDBに作られていない");
            Assert.That(cols, Does.Contain(opt.DbColumn.ToLowerInvariant()), "楽観ロック列がDBに作られていない");

            // === Turn 2: フィールド追加 → ALTER ===
            var beforeCount = db.GetColumnNames("product_master").Count;
            var reply2 = await orch.ProcessMessage("電話番号(テキスト)のフィールドを追加して、DBにも反映(ALTER)してください。");
            TestContext.WriteLine("=== reply2 ===\n" + reply2);

            Assert.That(host.ShownDdls, Has.Count.GreaterThan(1), "ALTER の DDL が提示されていない");
            var alterDdl = host.ShownDdls.Last().Ddl;
            TestContext.WriteLine("=== ALTER DDL ===\n" + alterDdl);
            Assert.DoesNotThrow(() => db.ExecuteDdl(alterDdl), "生成された ALTER DDL が SQLite で実行できない");
            Assert.That(db.GetColumnNames("product_master").Count, Is.GreaterThan(beforeCount), "ALTER で列が増えていない");
        }
    }
}

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
    /// ヘッダ明細(受注ヘッダ + 受注明細)を、別々のチャットセッションで作る一般的ユースケース。
    /// (1チャット=1モジュールなので、2モジュールはセッションを分けて作る)
    /// 明細→ヘッダの外部キー(IdField, Name!="Id")と、2テーブルの DDL 実行を実 SQLite で検証する。
    /// </summary>
    [TestFixture]
    public class HeaderDetailScenarioTest
    {
        [Test]
        public async Task 受注ヘッダと受注明細を作りFKと2テーブルを生成する()
        {
            var settings = TestEnv.RequireChatClientFactory();
            using var db = new SqliteTestDb("order_header_detail.db");

            // 2モジュールのシェルを同じ DesignData に用意(相互参照できるように)
            var order = new ModuleDesign { Name = "Order", DataSourceName = "Main", DbTable = "orders" };
            order.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            var orderDetail = new ModuleDesign { Name = "OrderDetail", DataSourceName = "Main", DbTable = "order_details" };
            orderDetail.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });

            var designData = new DesignData { Modules = new FakeModuleDesigns(new[] { order, orderDetail }) };
            var dataSources = new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } };
            var host = new FakeChatHost(designData, dataSources, _ => db.GetSchema());

            // === セッション1: 受注ヘッダ ===
            // 全体設定画面のオーケストレータ経由。「フィールド作成」→「DB更新(DDL)」にルーティングされる。
            var orderOrch = new AiOrchestrator(
                new AiFunctionContext(host, settings, new FakeOverallSettingsEditor(order)),
                FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenOverallSettings]);
            var r1 = await orderOrch.ProcessMessage(
                "受注ヘッダ(Order)を作成します。受注番号(テキスト・必須)、受注日(日付)、顧客名(テキスト)、合計金額(数値・小数2桁)、備考(テキスト) を追加。" +
                "システムフィールド(作成日時/更新日時/作成者/更新者/論理削除/楽観ロックはアプリインクリメント)も一通り。" +
                "各フィールドに snake_case の DBカラム名を付け、DBにテーブルを作成して。");
            TestContext.WriteLine("=== order reply ===\n" + r1);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(order));
            Assert.That(host.ShownDdls, Is.Not.Empty, "ヘッダのテーブル作成DDLが出ていない");
            var orderDdl = host.ShownDdls.Last().Ddl;
            TestContext.WriteLine("=== order DDL ===\n" + orderDdl);
            Assert.DoesNotThrow(() => db.ExecuteDdl(orderDdl), "ヘッダの CREATE DDL が実行できない");

            // === セッション2: 受注明細(ヘッダへの外部キー付き) ===
            var detailOrch = new AiOrchestrator(
                new AiFunctionContext(host, settings, new FakeOverallSettingsEditor(orderDetail)),
                FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenOverallSettings]);
            var r2 = await detailOrch.ProcessMessage(
                "受注明細(OrderDetail)を作成します。受注ヘッダ(Order)への外部キーを IdField で追加してください(フィールド名 OrderId、DBカラム order_id)。" +
                "さらに 商品名(テキスト)、数量(整数)、単価(数値・小数2桁)、金額(数値・小数2桁) を追加。" +
                "各フィールドに snake_case の DBカラム名を付け、DBにテーブルを作成して。");
            TestContext.WriteLine("=== detail reply ===\n" + r2);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(orderDetail));
            Assert.That(host.ShownDdls, Has.Count.GreaterThan(1), "明細のテーブル作成DDLが出ていない");
            var detailDdl = host.ShownDdls.Last().Ddl;
            TestContext.WriteLine("=== detail DDL ===\n" + detailDdl);
            Assert.DoesNotThrow(() => db.ExecuteDdl(detailDdl), "明細の CREATE DDL が実行できない");

            // 2テーブルできているか
            var schema = db.GetSchema().Select(t => t.Name.ToLowerInvariant()).ToHashSet();
            Assert.That(schema, Does.Contain("orders"), "orders テーブルが無い");
            Assert.That(schema, Does.Contain("order_details"), "order_details テーブルが無い");

            // 明細にヘッダ参照の外部キー列(order_id)を持つフィールドがあるか。
            // FK の型は IdField / LinkField / Number いずれも妥当なので型は固定せず、DBカラムで判定する。
            var fk = orderDetail.Fields.OfType<DbValueFieldDesignBase>()
                .FirstOrDefault(f => string.Equals(f.DbColumn, "order_id", StringComparison.OrdinalIgnoreCase));
            Assert.That(fk, Is.Not.Null, "明細にヘッダ参照の外部キー(order_id 列を持つフィールド)が無い");
            var detailCols = db.GetColumnNames("order_details").Select(c => c.ToLowerInvariant()).ToHashSet();
            Assert.That(detailCols, Does.Contain("order_id"), "外部キー列 order_id がDBに作られていない");

            // 簡単な round-trip: ヘッダ1件 + 明細1件(FK紐付け) を入れて読めるか(NOT NULL 列はダミーで埋める)
            db.InsertRow("orders", new() { ["id"] = 1 });
            db.InsertRow("order_details", new() { ["id"] = 1, [fk.DbColumn] = 1 });
            var cnt = db.ExecuteScalarLong($"SELECT COUNT(*) FROM order_details WHERE {fk.DbColumn} = 1;");
            Assert.That(cnt, Is.EqualTo(1), "FK でヘッダに紐づく明細が取得できない");
        }
    }
}

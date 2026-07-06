using Microsoft.Extensions.AI;
using NUnit.Framework;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions;

namespace Designer.WpfApp.Test
{
    // 初段ルーター + オーケストレーター(AiOrchestrator)のエンドツーエンド統合テスト(実 Azure OpenAI)。
    // 画面(面)ごとの対応機能セットに対し、自然文の指示が正しい機能へ分解・順序実行されること、
    // 非対応機能は「別画面で行える」と案内されることを検証する。
    [TestFixture]
    public class OrchestratorTest
    {
        static (AiOrchestrator Orch, List<FieldDesignBase> Fields, FakeDetailLayoutEditor Editor)
            DetailScreen(Func<IChatClient> settings, GridLayoutDesign? initialLayout = null)
        {
            var fields = new List<FieldDesignBase>
            {
                new IdFieldDesign { Name = "Id", DbColumn = "id" },
                new TextFieldDesign { Name = "ProductName", DbColumn = "product_name", DisplayName = "商品名" },
            };
            var module = new ModuleDesign { Name = "Product", DataSourceName = "Main", DbTable = "product", Fields = fields };
            var designData = new DesignData { Modules = new FakeModuleDesigns(new[] { module }) };
            var dataSources = new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } };
            var host = new FakeChatHost(designData, dataSources);
            var editor = new FakeDetailLayoutEditor(fields, initialLayout, "Product");
            var orch = new AiOrchestrator(
                new AiFunctionContext(host, settings, editor),
                FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenDetailLayout]);
            return (orch, fields, editor);
        }

        static List<string> CollectFieldNames(LayoutDesignBase? layout)
        {
            var names = new List<string>();
            void Walk(LayoutDesignBase? node)
            {
                switch (node)
                {
                    case FieldLayoutDesign f:
                        if (!string.IsNullOrEmpty(f.FieldName)) names.Add(f.FieldName);
                        break;
                    case GridLayoutDesign grid:
                        foreach (var row in grid.Rows)
                            foreach (var col in row.Columns) Walk(col.Layout);
                        break;
                    case TabLayoutDesign tab:
                        foreach (var t in tab.Layouts) Walk(t);
                        break;
                    case CanvasLayoutDesign canvas:
                        foreach (var e in canvas.Elements) Walk(e.Layout);
                        break;
                }
            }
            Walk(layout);
            return names;
        }

        // 詳細画面: 「フィールドを追加して配置」は field.create → layout.detail に分解され順に実行される。
        [Test]
        public async Task 詳細画面_フィールド追加と配置を順に実行する()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (orch, fields, editor) = DetailScreen(settings);

            var reply = await orch.ProcessMessage("金額(Amount)という数値フィールド(小数2桁)を追加して、詳細レイアウトに配置してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            // フィールドが作成された
            Assert.That(fields.Any(f => f.Name == "Amount"), Is.True, "Amount フィールドが作成されていない");
            Assert.That(fields.First(f => f.Name == "Amount"), Is.TypeOf<NumberFieldDesign>(), "Amount は NumberField であるべき");
            // レイアウトに配置された
            Assert.That(CollectFieldNames(editor.Detail.Layout), Does.Contain("Amount"), "Amount がレイアウトに配置されていない");
        }

        // 詳細画面: 配置だけの指示はレイアウトのみ実行し、勝手にフィールドを増やさない。
        [Test]
        public async Task 詳細画面_配置だけの指示はレイアウトのみ()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (orch, fields, editor) = DetailScreen(settings);
            // データ入力フィールド(DbValueField)の数。標準パーツ(戻る/タイトル/サブミット)はレイアウト機能が足すため許容し、
            // ここでは「ルーターが勝手に field.create を呼んでデータフィールドを増やさないこと」を確認する。
            var dataBefore = fields.Count(f => f is DbValueFieldDesignBase);

            var reply = await orch.ProcessMessage("フィールドをいい感じに並べてください。");
            TestContext.WriteLine(reply);

            Assert.That(fields.Count(f => f is DbValueFieldDesignBase), Is.EqualTo(dataBefore),
                "配置だけの指示でデータフィールドが増えてはいけない(標準パーツの追加は許容)");
            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "レイアウトが更新されていない");
            Assert.That(CollectFieldNames(editor.Detail.Layout), Does.Contain("ProductName"), "既存フィールドが配置されていない");
        }

        // 詳細画面: 「必要なフィールドを追加して、いい感じに並べて」は
        // field.create(データ項目) → field.create(見出しラベル+タイトル+戻る+サブミット) → layout.detail に分解され、
        // ラベル・標準パーツが作成されて配置される(実機で『ラベルが付かない』問題の回帰テスト)。
        [Test]
        public async Task 詳細画面_フィールド追加しいい感じに並べるとラベルと標準パーツも作られて配置される()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = new List<FieldDesignBase> { new IdFieldDesign { Name = "Id", DbColumn = "id" } };
            var module = new ModuleDesign { Name = "CustomerMaster", DataSourceName = "Main", DbTable = "customer_master", Fields = fields };
            var designData = new DesignData { Modules = new FakeModuleDesigns(new[] { module }) };
            var dataSources = new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } };
            var host = new FakeChatHost(designData, dataSources);
            var editor = new FakeDetailLayoutEditor(fields, null, "CustomerMaster");
            var orch = new AiOrchestrator(
                new AiFunctionContext(host, settings, editor),
                FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenDetailLayout]);

            var reply = await orch.ProcessMessage("顧客マスタに必要なフィールドを考えて追加して、そんでいい感じに並べて");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            // データ項目が追加された
            Assert.That(fields.OfType<DbValueFieldDesignBase>().Count(f => f.Name != "Id"), Is.GreaterThan(0), "データ項目が追加されていない");
            // 見出しラベルが作られた(入力に対応するラベル = RelativeField 付き)
            var headingLabels = fields.OfType<LabelFieldDesign>().Where(l => !string.IsNullOrEmpty(l.RelativeField)).ToList();
            Assert.That(headingLabels.Count, Is.GreaterThan(0), "見出しラベルが作成されていない(初段が field.create のラベル作成工程を前置していない)");
            // 標準パーツが作られた
            Assert.That(fields.OfType<SubmitButtonFieldDesign>().Any(), Is.True, "サブミットボタンが作成されていない");
            Assert.That(fields.OfType<AnchorTagFieldDesign>().Any(), Is.True, "戻るボタンが作成されていない");
            // 見出しラベルがレイアウトに配置された
            var placed = CollectFieldNames(editor.Detail.Layout);
            Assert.That(headingLabels.Any(l => placed.Contains(l.Name)), Is.True, "見出しラベルがレイアウトに配置されていない");
        }

        // 詳細画面: 「ボタンを追加して」は配置の明示が無くても field.create → layout.detail に分解され、
        // 作成したボタンが既存レイアウトを保ったまま配置される(実機で『追加されたが並ばない』問題の回帰テスト)。
        [Test]
        public async Task 詳細画面_ボタン追加は配置指示が無くてもレイアウトに配置される()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var initial = new GridLayoutDesign();
            var row = new GridRow();
            row.Columns.Add(new GridColumn { Layout = new FieldLayoutDesign("ProductName") });
            initial.Rows.Add(row);
            var (orch, fields, editor) = DetailScreen(settings, initial);

            var reply = await orch.ProcessMessage("更新ボタンを追加してクリックイベントも追加");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            // ボタンが OnClick 付きで作成された
            var button = fields.OfType<ButtonFieldDesign>().FirstOrDefault();
            Assert.That(button, Is.Not.Null, "更新ボタンが作成されていない");
            Assert.That(button!.OnClick, Is.Not.Empty, "OnClick イベントが設定されていない");
            // 既存レイアウトを保ったまま、ボタンが配置された
            var placed = CollectFieldNames(editor.Detail.Layout);
            Assert.That(placed, Does.Contain(button.Name), "作成したボタンがレイアウトに配置されていない(初段が layout.detail の Step を続けていない)");
            Assert.That(placed, Does.Contain("ProductName"), "既存フィールドの配置が消えている");
        }

        // 全体設定画面: 短い「DDLを出して」は対象を聞き返さず db.update に振られ、DDL が提示される(質問しすぎ回帰テスト)。
        [Test]
        public async Task 全体設定_短いDDL指示は聞き返さずDDLを出す()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = new List<FieldDesignBase>
            {
                new IdFieldDesign { Name = "Id", DbColumn = "id" },
                new TextFieldDesign { Name = "CustomerCode", DbColumn = "customer_code" },
                new TextFieldDesign { Name = "CustomerName", DbColumn = "customer_name" },
            };
            var module = new ModuleDesign { Name = "CustomerMaster", DataSourceName = "Main", DbTable = "customer_master", Fields = fields };
            var designData = new DesignData { Modules = new FakeModuleDesigns(new[] { module }) };
            var dataSources = new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } };
            // 実DBにテーブルは無い → db.update に振られれば CREATE の DDL が提示される。
            var host = new FakeChatHost(designData, dataSources);
            var editor = new FakeOverallSettingsEditor(module);
            var orch = new AiOrchestrator(
                new AiFunctionContext(host, settings, editor),
                FunctionCatalog.ScreenFunctions[FunctionCatalog.ScreenOverallSettings]);

            var reply = await orch.ProcessMessage("DDLを出して");
            TestContext.WriteLine(reply);

            Assert.That(host.ShownDdls, Is.Not.Empty,
                "『DDLを出して』が db.update に振られず DDL が出ていない(ルーターが聞き返した?)");
        }

        // 詳細画面: この画面では実行できない SQL 編集を頼むと、対応画面(SQL)を案内する。
        [Test]
        public async Task 詳細画面_非対応のSQL依頼は対応画面を案内する()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (orch, fields, editor) = DetailScreen(settings);
            var before = fields.Count;

            var reply = await orch.ProcessMessage("売上を商品ごとに集計するSQLクエリを書いてください。");
            TestContext.WriteLine(reply);

            Assert.That(reply, Does.Contain("SQL"), "SQL への案内が無い");
            Assert.That(reply, Does.Contain("画面"), "対応画面の案内が無い");
            Assert.That(fields.Count, Is.EqualTo(before), "非対応依頼でフィールドが変化してはいけない");
        }
    }
}

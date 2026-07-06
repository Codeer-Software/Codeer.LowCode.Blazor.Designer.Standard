using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// SearchLayoutChat の検証。
    /// 検索レイアウトは詳細レイアウトとほぼ同じフィールド配置に加えて、And/Or のネスト
    /// (SearchGridLayoutDesign.Operator)がある。この And/Or 構造が正しく作られること
    /// (Stage1 の専用プロンプト → コードで骨組み構築)を主眼に検証する。
    /// 実 AI を呼ぶため、AZURE_OPENAI_* 未設定時は Ignore される。
    /// </summary>
    [TestFixture]
    public class SearchLayoutTest
    {
        // レイアウト木から参照されている FieldName を全部集める。
        static HashSet<string> CollectFieldNames(LayoutDesignBase? layout)
        {
            var names = new HashSet<string>();
            void Walk(LayoutDesignBase? node)
            {
                switch (node)
                {
                    case FieldLayoutDesign f:
                        if (!string.IsNullOrEmpty(f.FieldName)) names.Add(f.FieldName);
                        break;
                    case GridLayoutDesign grid:  // SearchGridLayoutDesign も派生として含む
                        foreach (var row in grid.Rows)
                            foreach (var col in row.Columns)
                                Walk(col.Layout);
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

        // すべての SearchGridLayoutDesign ノードを集める。
        static List<SearchGridLayoutDesign> CollectSearchGrids(LayoutDesignBase? layout)
        {
            var list = new List<SearchGridLayoutDesign>();
            void Walk(LayoutDesignBase? node)
            {
                switch (node)
                {
                    case SearchGridLayoutDesign sg:
                        list.Add(sg);
                        foreach (var row in sg.Rows)
                            foreach (var col in row.Columns) Walk(col.Layout);
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
            return list;
        }

        static List<FieldDesignBase> CustomerFields() => new()
        {
            new IdFieldDesign { Name = "Id", DbColumn = "id" },
            new TextFieldDesign { Name = "PersonInCharge", DbColumn = "person_in_charge", DisplayName = "担当者名" },
            new TextFieldDesign { Name = "CustomerCode", DbColumn = "customer_code", DisplayName = "顧客コード" },
            new TextFieldDesign { Name = "CustomerName", DbColumn = "customer_name", DisplayName = "顧客名" },
            new SelectFieldDesign { Name = "Status", DbColumn = "status", DisplayName = "ステータス" },
        };

        [Test]
        public async Task 単純なAND検索_全フィールドが配置される()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = CustomerFields();
            var editor = new FakeSearchLayoutEditor(fields);
            var chat = new SearchLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("担当者名・顧客コード・ステータスで検索できるようにしてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Search.Layout));

            var placed = CollectFieldNames(editor.Search.Layout);
            foreach (var f in new[] { "PersonInCharge", "CustomerCode", "Status" })
                Assert.That(placed, Does.Contain(f), $"{f} が配置されていない");

            // 単純AND: ルートは And、ネストした Or/And グループは無い(ルート1つだけ)。
            var grids = CollectSearchGrids(editor.Search.Layout);
            Assert.That(grids, Has.Count.EqualTo(1), "単純ANDなのに条件グループがネストされている");
            Assert.That(grids[0].Operator, Is.EqualTo(SearchOperator.And));

            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<SearchLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.Search)), "レイアウトが壊れている");
        }

        // ユーザーの例そのもの: 担当者名 && (顧客コード || 顧客名)
        [Test]
        public async Task AndとOrのネスト_担当者名かつ顧客コードまたは顧客名()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = CustomerFields();
            var editor = new FakeSearchLayoutEditor(fields);
            var chat = new SearchLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage(
                "担当者名で絞り込み、かつ 顧客コードか顧客名のどちらかで絞り込めるようにしてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Search.Layout));

            var placed = CollectFieldNames(editor.Search.Layout);
            foreach (var f in new[] { "PersonInCharge", "CustomerCode", "CustomerName" })
                Assert.That(placed, Does.Contain(f), $"{f} が配置されていない");

            var grids = CollectSearchGrids(editor.Search.Layout);
            // ルート And + ネストした Or の2つ以上のグループがある。
            Assert.That(grids.Any(g => g.Operator == SearchOperator.And), Is.True, "And グループが無い");
            var orGroup = grids.FirstOrDefault(g => g.Operator == SearchOperator.Or);
            Assert.That(orGroup, Is.Not.Null, "Or グループ(顧客コード||顧客名)が作られていない");

            // Or グループの中身は 顧客コード・顧客名 で、担当者名は入らない。
            var orFields = CollectFieldNames(orGroup);
            Assert.That(orFields, Does.Contain("CustomerCode"), "Or グループに顧客コードが無い");
            Assert.That(orFields, Does.Contain("CustomerName"), "Or グループに顧客名が無い");
            Assert.That(orFields, Does.Not.Contain("PersonInCharge"), "担当者名が Or グループに入っている(構造が誤り)");

            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<SearchLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.Search)), "レイアウトが壊れている");
        }
    }
}

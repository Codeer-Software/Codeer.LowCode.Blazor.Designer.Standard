using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// 詳細レイアウトのプラン方式(一から全体配置: Stage1プラン→コードで決定的レンダリング)の決定的テスト。
    /// 実 AI を使わず ScriptedChatClient でプラン JSON を返し、レンダリング結果の構造を検証する。
    /// </summary>
    [TestFixture]
    public class DetailLayoutPlanUnitTest
    {
        static List<FieldDesignBase> MakeFields() => new()
        {
            new IdFieldDesign { Name = "Id", DbColumn = "id" },
            new TextFieldDesign { Name = "Code", DbColumn = "code" },
            new TextFieldDesign { Name = "ProductName", DbColumn = "name", DisplayName = "商品名" },
            new NumberFieldDesign { Name = "Price", DbColumn = "price" },
            new BooleanFieldDesign { Name = "Active", DbColumn = "active" },
            new TextFieldDesign { Name = "Note", DbColumn = "note", IsMultiline = true },
            new DateTimeFieldDesign { Name = "CreatedAt", DbColumn = "created_at" },
            new LabelFieldDesign { Name = "Title", Text = "商品", Style = LabelStyle.H4 },
            new AnchorTagFieldDesign { Name = "BackButton", Target = AnchorTarget.HistoryBack },
            new SubmitButtonFieldDesign { Name = "Submit", Text = "登録" },
        };

        static string PlanResponse(string tabsJson)
            => $"{{\"Tabs\":{tabsJson},\"CanDo\":true,\"Explanation\":\"並べました\"}}";

        const string SimplePlan = "[{\"Title\":\"\",\"Sections\":[{\"Title\":\"\",\"IsCard\":false,\"ItemsPerRow\":1,\"Fields\":[\"Code\",\"ProductName\",\"Price\",\"Active\",\"Note\"]}]}]";

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
                    case GridLayoutDesign g:
                        foreach (var row in g.Rows)
                            foreach (var col in row.Columns) Walk(col.Layout);
                        break;
                    case TabLayoutDesign tab:
                        foreach (var t in tab.Layouts) Walk(t);
                        break;
                }
            }
            Walk(layout);
            return names;
        }

        [Test]
        public async Task 一から全体配置_プラン1回でレンダリングされ標準パーツとラベルが決定的に入る()
        {
            var fields = MakeFields();
            var editor = new FakeDetailLayoutEditor(fields);
            var client = new ScriptedChatClient(PlanResponse(SimplePlan));
            var chat = new DetailLayoutFunction(() => client, editor);

            var result = await chat.ExecuteAsync("いい感じに並べて");

            Assert.That(client.Requests, Has.Count.EqualTo(1), "プラン1回で完了するはず");
            Assert.That(result.Outcome, Is.EqualTo(FunctionOutcome.Done));
            Assert.That(editor.UpdateCount, Is.EqualTo(1));

            var root = (GridLayoutDesign)editor.Detail.Layout!;
            var placed = CollectFieldNames(root);

            //標準パーツ: 先頭行=戻る+タイトル、末尾行=サブミット
            Assert.That(CollectFieldNames(new GridLayoutDesign { Rows = { root.Rows[0] } }),
                Is.EquivalentTo(new[] { "BackButton", "Title" }), "先頭行に戻る+タイトルが無い");
            Assert.That(CollectFieldNames(new GridLayoutDesign { Rows = { root.Rows[^1] } }),
                Is.EquivalentTo(new[] { "Submit" }), "末尾行にサブミットが無い");

            //システムフィールドは置かれない(プランの選択肢にも入らない)
            Assert.That(placed, Does.Not.Contain("Id"));
            Assert.That(placed, Does.Not.Contain("CreatedAt"));
            var planPrompt = string.Join("\n", client.Requests[0].Select(m => m.Text));
            Assert.That(planPrompt, Does.Not.Contain("CreatedAt"), "システムフィールドがプランの選択肢に出ている");

            //ラベル左: 入力フィールドにはラベルが自動生成される(Text空+RelativeField)
            var codeLabel = fields.OfType<LabelFieldDesign>().FirstOrDefault(l => l.RelativeField == "Code");
            Assert.That(codeLabel, Is.Not.Null, "Code のラベルが生成されていない");
            Assert.That(placed, Does.Contain(codeLabel!.Name));

            //Boolean にはラベルを付けない
            Assert.That(fields.OfType<LabelFieldDesign>().Any(l => l.RelativeField == "Active"), Is.False, "Boolean にラベルが付いている");

            //複数行テキスト(Note)は単独行・全幅(同じ行に他の列が無い)
            var noteRow = root.Rows.FirstOrDefault(r => r.Columns.Any(c => c.Layout is FieldLayoutDesign f && f.FieldName == "Note"));
            Assert.That(noteRow, Is.Not.Null);
            Assert.That(noteRow!.Columns, Has.Count.EqualTo(1), "全幅フィールドの行に他の列がある");

            //ラベル左のラベル列幅が正規化されている(>=96)
            var labelCols = root.Rows.SelectMany(r => r.Columns)
                .Where(c => c.Layout is FieldLayoutDesign f && f.FieldName == codeLabel.Name).ToList();
            Assert.That(labelCols[0].Width, Is.GreaterThanOrEqualTo(96), "ラベル列幅が正規化されていない");
        }

        [Test]
        public async Task プランに未知フィールド_差し戻して再取得する()
        {
            var fields = MakeFields();
            var editor = new FakeDetailLayoutEditor(fields);
            var client = new ScriptedChatClient(
                PlanResponse("[{\"Title\":\"\",\"Sections\":[{\"Title\":\"\",\"IsCard\":false,\"ItemsPerRow\":1,\"Fields\":[\"Nope\"]}]}]"),
                PlanResponse(SimplePlan));
            var chat = new DetailLayoutFunction(() => client, editor);

            var result = await chat.ExecuteAsync("いい感じに並べて");

            Assert.That(client.Requests, Has.Count.EqualTo(2), "未知フィールドで差し戻されていない");
            Assert.That(result.Outcome, Is.EqualTo(FunctionOutcome.Done));
            Assert.That(CollectFieldNames(editor.Detail.Layout), Does.Contain("Code"));
        }

        [Test]
        public async Task タブ指定_TabLayoutDesignが組まれる()
        {
            var fields = MakeFields();
            var editor = new FakeDetailLayoutEditor(fields);
            var client = new ScriptedChatClient(PlanResponse(
                "[{\"Title\":\"基本\",\"Sections\":[{\"Title\":\"\",\"IsCard\":false,\"ItemsPerRow\":1,\"Fields\":[\"Code\",\"ProductName\"]}]}," +
                "{\"Title\":\"その他\",\"Sections\":[{\"Title\":\"\",\"IsCard\":false,\"ItemsPerRow\":1,\"Fields\":[\"Price\",\"Note\"]}]}]"));
            var chat = new DetailLayoutFunction(() => client, editor);

            var result = await chat.ExecuteAsync("タブに分けていい感じに並べて");

            Assert.That(result.Outcome, Is.EqualTo(FunctionOutcome.Done));
            var root = (GridLayoutDesign)editor.Detail.Layout!;
            var tab = root.Rows.SelectMany(r => r.Columns).Select(c => c.Layout).OfType<TabLayoutDesign>().FirstOrDefault();
            Assert.That(tab, Is.Not.Null, "TabLayoutDesign が組まれていない");
            Assert.That(tab!.Tabs, Is.EqualTo(new[] { "基本", "その他" }));
            Assert.That(tab.Layouts, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task カードセクション_IsBorderedと見出しラベルが入る()
        {
            var fields = MakeFields();
            var editor = new FakeDetailLayoutEditor(fields);
            var client = new ScriptedChatClient(PlanResponse(
                "[{\"Title\":\"\",\"Sections\":[" +
                "{\"Title\":\"基本情報\",\"IsCard\":true,\"ItemsPerRow\":1,\"Fields\":[\"Code\",\"ProductName\"]}," +
                "{\"Title\":\"金額\",\"IsCard\":true,\"ItemsPerRow\":1,\"Fields\":[\"Price\"]}]}]"));
            var chat = new DetailLayoutFunction(() => client, editor);

            var result = await chat.ExecuteAsync("カードに分けて配置して");

            Assert.That(result.Outcome, Is.EqualTo(FunctionOutcome.Done));
            var root = (GridLayoutDesign)editor.Detail.Layout!;
            var cards = root.Rows.SelectMany(r => r.Columns).Select(c => c.Layout)
                .OfType<GridLayoutDesign>().Where(g => g.IsBordered).ToList();
            Assert.That(cards, Has.Count.EqualTo(2), "カード(IsBordered)セクションが2つ無い");
            var heading = fields.OfType<LabelFieldDesign>().FirstOrDefault(l => l.Text == "基本情報");
            Assert.That(heading, Is.Not.Null, "セクション見出しラベルが生成されていない");
            Assert.That(heading!.Style, Is.EqualTo(LabelStyle.H5));
        }

        [Test]
        public async Task CanDoFalse_プランを諦めて従来の単段生成へフォールバックする()
        {
            var fields = MakeFields();
            var editor = new FakeDetailLayoutEditor(fields);
            var client = new ScriptedChatClient(
                "{\"Tabs\":[],\"CanDo\":false,\"Explanation\":\"プランでは表現できません。\"}",
                //フォールバック先(単段生成)の応答: 変更なしの断り
                "{\"Layout\":{\"Rows\":[]},\"Explanation\":\"この指示には対応できません。\"}");
            var chat = new DetailLayoutFunction(() => client, editor);

            var result = await chat.ExecuteAsync("いい感じに並べて");

            Assert.That(client.Requests, Has.Count.EqualTo(2), "CanDo=false で従来方式へフォールバックしていない");
            //2回目のリクエストが従来方式(現在のレイアウトを渡す単段プロンプト)であること
            var fallbackPrompt = string.Join("\n", client.Requests[1].Select(m => m.Text));
            Assert.That(fallbackPrompt, Does.Contain("現在のレイアウト"));
        }

        [Test]
        public async Task 罫線などの細かい見た目指定_プラン経路を使わず従来方式に行く()
        {
            var fields = MakeFields();
            var editor = new FakeDetailLayoutEditor(fields);
            var client = new ScriptedChatClient(
                "{\"Layout\":{\"Rows\":[]},\"Explanation\":\"変更なし\"}");
            var chat = new DetailLayoutFunction(() => client, editor);

            await chat.ExecuteAsync("罫線を引いて配置して");

            //1回目のリクエストが従来方式(レイアウト仕様つきの単段プロンプト)であること
            var prompt = string.Join("\n", client.Requests[0].Select(m => m.Text));
            Assert.That(prompt, Does.Contain("現在のレイアウト"), "細かい見た目指定なのにプラン経路に入っている");
            Assert.That(prompt, Does.Not.Contain("配置プランナー"));
        }
    }
}

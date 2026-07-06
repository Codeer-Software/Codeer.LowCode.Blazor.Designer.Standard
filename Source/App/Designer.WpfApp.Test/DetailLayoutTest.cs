using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// 復活させた DetailLayoutFunction を自動改善ハーネスに載せる。
    /// ①「いい感じに配置」= 良型パターンの適用を検証する(空行・宙ぶらりん参照が無い／主要フィールドが置かれる／壊れない)。
    /// ② 細かい指定(罫線等)は別途。
    /// </summary>
    [TestFixture]
    public class DetailLayoutTest
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
                    case GridLayoutDesign grid:
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

        // 実機(顧客マスタ)の再現: 既存レイアウト(1行×4空セル)＋ラベル無し＋システム列込み で「いい感じに並べて」。
        [Test]
        public async Task 既存レイアウトありで_いい感じに並べ直す()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = new List<FieldDesignBase>
            {
                new IdFieldDesign { Name = "Id", DbColumn = "id" },
                new TextFieldDesign { Name = "CustomerCode", DbColumn = "customer_code", DisplayName = "顧客コード" },
                new TextFieldDesign { Name = "CustomerName", DbColumn = "customer_name", DisplayName = "顧客名" },
                new TextFieldDesign { Name = "CustomerNameKana", DbColumn = "customer_name_kana", DisplayName = "フリガナ" },
                new TextFieldDesign { Name = "PostalCode", DbColumn = "postal_code", DisplayName = "郵便番号" },
                new TextFieldDesign { Name = "Address", DbColumn = "address", DisplayName = "住所" },
                new TextFieldDesign { Name = "PhoneNumber", DbColumn = "phone_number", DisplayName = "電話番号" },
                new TextFieldDesign { Name = "Email", DbColumn = "email", DisplayName = "メール" },
                new TextFieldDesign { Name = "ContactPerson", DbColumn = "contact_person", DisplayName = "担当者" },
                new TextFieldDesign { Name = "Memo", DbColumn = "memo", DisplayName = "メモ", IsMultiline = true },
                new DateTimeFieldDesign { Name = "CreatedAt", DbColumn = "created_at" },
                new DateTimeFieldDesign { Name = "UpdatedAt", DbColumn = "updated_at" },
            };
            // レイアウト機能は既存の見出しラベルを配置するのみ(作成しない=field.createの担当)。各入力に対応するラベルを事前投入する。
            foreach (var name in new[] { "CustomerCode", "CustomerName", "CustomerNameKana", "PostalCode", "Address", "PhoneNumber", "Email", "ContactPerson", "Memo" })
                fields.Add(new LabelFieldDesign { Name = name + "Label", Text = "", RelativeField = name });
            // 既存の "1行×4空セル" レイアウト
            var starter = new GridLayoutDesign();
            var row = new GridRow();
            for (var i = 0; i < 4; i++) row.Columns.Add(new GridColumn());
            starter.Rows.Add(row);

            var editor = new FakeDetailLayoutEditor(fields, starter);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("フィールドをいい感じに並べてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            var placed = CollectFieldNames(editor.Detail.Layout);
            // 主要な入力フィールドが配置されている
            foreach (var f in new[] { "CustomerCode", "CustomerName", "PostalCode", "Address", "Email" })
                Assert.That(placed, Does.Contain(f), $"{f} が配置されていない");
            // 既存の見出しラベルが配置されている(label+input フォームになっている)
            var placedLabels = fields.OfType<LabelFieldDesign>().Where(l => placed.Contains(l.Name)).ToList();
            Assert.That(placedLabels, Is.Not.Empty, "ラベルが配置されていない(ラベル無しの素並び)");
            // システム列(Id/CreatedAt/UpdatedAt)は配置しない
            foreach (var sys in new[] { "Id", "CreatedAt", "UpdatedAt" })
                Assert.That(placed, Does.Not.Contain(sys), $"システム列 {sys} を配置している");
            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<DetailLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.Detail)), "レイアウトが壊れている");
        }

        static List<FieldDesignBase> MasterFieldsWithSystemColumns() => new()
        {
            new IdFieldDesign { Name = "Id", DbColumn = "id" },
            new TextFieldDesign { Name = "CustomerCode", DbColumn = "customer_code", DisplayName = "顧客コード" },
            new TextFieldDesign { Name = "CustomerName", DbColumn = "customer_name", DisplayName = "顧客名" },
            new TextFieldDesign { Name = "PostalCode", DbColumn = "postal_code", DisplayName = "郵便番号" },
            new TextFieldDesign { Name = "Address", DbColumn = "address", DisplayName = "住所" },
            new TextFieldDesign { Name = "Email", DbColumn = "email", DisplayName = "メール" },
            new DateTimeFieldDesign { Name = "CreatedAt", DbColumn = "created_at" },
            new DateTimeFieldDesign { Name = "UpdatedAt", DbColumn = "updated_at" },
            new TextFieldDesign { Name = "Creator", DbColumn = "creator" },
            new TextFieldDesign { Name = "Updater", DbColumn = "updater" },
            new BooleanFieldDesign { Name = "LogicalDelete", DbColumn = "logical_delete" },
            new NumberFieldDesign { Name = "OptimisticLocking", DbColumn = "version" },
        };

        // システムフィールド(Id/CreatedAt/... )が一覧に在っても、明示要求が無ければ詳細レイアウトに置かれない。
        // AIが指示を無視して置いても、最終的に決定的な除去ガードで取り除かれることを確認(ラベル有無には依存しない)。
        [Test]
        public async Task システムフィールドは明示要求が無ければレイアウトに配置されない()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = MasterFieldsWithSystemColumns();
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("フィールドをいい感じに並べてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            var placed = CollectFieldNames(editor.Detail.Layout);
            foreach (var sys in new[] { "Id", "CreatedAt", "UpdatedAt", "Creator", "Updater", "LogicalDelete", "OptimisticLocking" })
                Assert.That(placed, Does.Not.Contain(sys), $"システム列 {sys} がレイアウトに配置されている");
            // 業務フィールドはちゃんと残っている(除去のしすぎで空にならない)
            Assert.That(placed, Does.Contain("CustomerCode"));
            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<DetailLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.Detail)), "レイアウトが壊れている");
        }

        // 逆に、ユーザーが明示的に表示を求めたシステムフィールドは配置してよい(除去ガードが効きすぎない)。
        [Test]
        public async Task 明示的に表示要求したシステムフィールドはレイアウトに配置できる()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = MasterFieldsWithSystemColumns();
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage(
                "フィールドをいい感じに並べてください。ただし CreatedAt（作成日時）も画面に表示したいので配置してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            var placed = CollectFieldNames(editor.Detail.Layout);
            Assert.That(placed, Does.Contain("CreatedAt"), "明示要求した CreatedAt が配置されていない");
            // 明示要求していない他のシステム列は依然として置かれない
            foreach (var sys in new[] { "Id", "UpdatedAt", "LogicalDelete", "OptimisticLocking" })
                Assert.That(placed, Does.Not.Contain(sys), $"明示要求していないシステム列 {sys} が配置されている");
        }

        static IEnumerable<GridColumn> AllColumns(LayoutDesignBase? layout)
        {
            if (layout is GridLayoutDesign grid)
                foreach (var row in grid.Rows)
                    foreach (var col in row.Columns)
                    {
                        yield return col;
                        foreach (var c in AllColumns(col.Layout)) yield return c;
                    }
            else if (layout is TabLayoutDesign tab)
                foreach (var t in tab.Layouts) foreach (var c in AllColumns(t)) yield return c;
        }

        static bool AnyMultiColumnRow(LayoutDesignBase? layout)
        {
            if (layout is GridLayoutDesign grid)
            {
                foreach (var row in grid.Rows)
                {
                    if (row.Columns.Count >= 2) return true;
                    foreach (var col in row.Columns) if (AnyMultiColumnRow(col.Layout)) return true;
                }
            }
            else if (layout is TabLayoutDesign tab)
                foreach (var t in tab.Layouts) if (AnyMultiColumnRow(t)) return true;
            return false;
        }

        [Test]
        public async Task ラベル上スタイル_ラベル列に幅を付けず複数列で並べる()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = new List<FieldDesignBase>
            {
                new IdFieldDesign { Name = "Id", DbColumn = "id" },
                new TextFieldDesign { Name = "CustomerCode", DbColumn = "customer_code", DisplayName = "顧客コード" },
                new TextFieldDesign { Name = "CustomerName", DbColumn = "customer_name", DisplayName = "顧客名" },
                new TextFieldDesign { Name = "PostalCode", DbColumn = "postal_code", DisplayName = "郵便番号" },
                new TextFieldDesign { Name = "Address", DbColumn = "address", DisplayName = "住所" },
                new TextFieldDesign { Name = "PhoneNumber", DbColumn = "phone_number", DisplayName = "電話番号" },
                new TextFieldDesign { Name = "Email", DbColumn = "email", DisplayName = "メール" },
            };
            // レイアウト機能は既存の見出しラベルを配置するのみ。各入力に対応するラベルを事前投入する。
            foreach (var name in new[] { "CustomerCode", "CustomerName", "PostalCode", "Address", "PhoneNumber", "Email" })
                fields.Add(new LabelFieldDesign { Name = name + "Label", Text = "", RelativeField = name });
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("いい感じに並べてください。ラベルはフィールドの上に置くスタイルで。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            var labels = fields.OfType<LabelFieldDesign>().ToList();
            Assert.That(labels, Is.Not.Empty, "ラベルが無い");
            // ラベル上スタイルではラベル列に幅を付けない
            var labelNames = labels.Select(l => l.Name).ToHashSet();
            foreach (var col in AllColumns(editor.Detail.Layout))
                if (col.Layout is FieldLayoutDesign f && labelNames.Contains(f.FieldName))
                    Assert.That(col.Width, Is.Null, $"ラベル列 {f.FieldName} に幅が付いている(上配置では不要)");

            // 【本質】ラベルは入力の「上」= 入力と別の行にある。同じ行に label|input が横並び(ラベル左)になっていたら失敗。
            var checkedPairs = 0;
            foreach (var label in labels)
            {
                var inputName = !string.IsNullOrEmpty(label.RelativeField) ? label.RelativeField
                    : label.Name.EndsWith("Label") ? label.Name[..^"Label".Length] : null;
                if (string.IsNullOrEmpty(inputName)) continue;
                var labelRow = RowOf(editor.Detail.Layout, label.Name);
                var inputRow = RowOf(editor.Detail.Layout, inputName);
                if (labelRow == null || inputRow == null) continue;
                checkedPairs++;
                Assert.That(ReferenceEquals(labelRow, inputRow), Is.False,
                    $"{label.Name} と {inputName} が同じ行(ラベル左/横並び)。ラベル上スタイルでは別の行(ラベルが上)にすること");
            }
            Assert.That(checkedPairs, Is.GreaterThan(0), "ラベルと入力の対応が検出できなかった");
            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<DetailLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.Detail)), "レイアウトが壊れている");
        }

        [Test]
        public async Task いい感じにフォームを配置する()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = new List<FieldDesignBase>
            {
                new IdFieldDesign { Name = "Id", DbColumn = "id" },
                new TextFieldDesign { Name = "ProductCode", DbColumn = "product_code", DisplayName = "商品コード" },
                new TextFieldDesign { Name = "Name", DbColumn = "name", DisplayName = "商品名" },
                new NumberFieldDesign { Name = "Price", DbColumn = "price", DisplayName = "単価" },
                new TextFieldDesign { Name = "Description", DbColumn = "description", DisplayName = "説明", IsMultiline = true },
            };
            // レイアウト機能は既存の見出しラベルを配置するのみ。各入力に対応するラベルを事前投入する。
            foreach (var name in new[] { "ProductCode", "Name", "Price", "Description" })
                fields.Add(new LabelFieldDesign { Name = name + "Label", Text = "", RelativeField = name });
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage(
                "このモジュールの詳細画面を、いい感じに配置してください。各入力にラベルを付けた見やすいフォームにして。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "レイアウトが適用されていない");
            var grid = editor.Detail.Layout as GridLayoutDesign;
            Assert.That(grid, Is.Not.Null, "ルートが GridLayoutDesign でない");
            Assert.That(grid!.Rows.Count, Is.GreaterThan(0), "行が無い");
            // 列の無い行が無い(空行は不具合)
            Assert.That(grid.Rows.All(r => r.Columns.Count > 0), Is.True, "列の無い行がある");

            // 主要な入力フィールドが配置されている
            var placed = CollectFieldNames(editor.Detail.Layout);
            foreach (var f in new[] { "ProductCode", "Name", "Price", "Description" })
                Assert.That(placed, Does.Contain(f), $"{f} が配置されていない");

            // レイアウトが壊れていない(シリアライズ往復)
            var json = JsonConverterEx.SerializeObject(editor.Detail);
            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<DetailLayoutDesign>(json), "レイアウトが壊れている");
        }

        static int CountBorderedGrids(LayoutDesignBase? layout)
        {
            var count = 0;
            void Walk(LayoutDesignBase? node)
            {
                if (node is GridLayoutDesign grid)
                {
                    if (grid.IsBordered) count++;
                    foreach (var row in grid.Rows)
                        foreach (var col in row.Columns)
                            Walk(col.Layout);
                }
                else if (node is TabLayoutDesign tab) foreach (var t in tab.Layouts) Walk(t);
                else if (node is CanvasLayoutDesign canvas) foreach (var e in canvas.Elements) Walk(e.Layout);
            }
            Walk(layout);
            return count;
        }

        static HashSet<string> NamesInRow(GridRow row)
        {
            var s = new HashSet<string>();
            foreach (var c in row.Columns)
                foreach (var n in CollectFieldNames(c.Layout))
                    s.Add(n);
            return s;
        }

        static GridColumn? FindColumn(LayoutDesignBase? layout, string fieldName)
        {
            if (layout is GridLayoutDesign grid)
            {
                foreach (var row in grid.Rows)
                    foreach (var col in row.Columns)
                    {
                        if (col.Layout is FieldLayoutDesign f && f.FieldName == fieldName) return col;
                        var nested = FindColumn(col.Layout, fieldName);
                        if (nested != null) return nested;
                    }
            }
            else if (layout is TabLayoutDesign tab)
                foreach (var t in tab.Layouts) { var n = FindColumn(t, fieldName); if (n != null) return n; }
            return null;
        }

        static bool HasCellBorder(GridColumn? c)
        {
            var b = c?.BorderStyle;
            return b != null && ((b.Left ?? 0) > 0 || (b.Top ?? 0) > 0 || (b.Right ?? 0) > 0 || (b.Bottom ?? 0) > 0);
        }

        [Test]
        public async Task 特定Gridのカラムだけにセル罫線を引く()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = new List<FieldDesignBase>
            {
                new IdFieldDesign { Name = "Id", DbColumn = "id" },
                new TextFieldDesign { Name = "ProductCode", DbColumn = "product_code", DisplayName = "商品コード" },
                new TextFieldDesign { Name = "Name", DbColumn = "name", DisplayName = "商品名" },
                new NumberFieldDesign { Name = "Price", DbColumn = "price", DisplayName = "単価" },
                new NumberFieldDesign { Name = "Stock", DbColumn = "stock", DisplayName = "在庫" },
            };
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage(
                "商品コード(ProductCode)と商品名(Name)を1つのネストした Grid にまとめ、その Grid 内のセル(カラム)に罫線(BorderStyle)を引いてください。" +
                "Price と Stock は通常配置のままで、罫線は付けないでください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない");
            var placed = CollectFieldNames(editor.Detail.Layout);
            foreach (var f in new[] { "ProductCode", "Name", "Price", "Stock" })
                Assert.That(placed, Does.Contain(f), $"{f} が配置されていない");

            // 対象(ProductCode/Name)のセルには罫線、対象外(Price/Stock)には罫線なし(スコープ厳守)
            Assert.That(HasCellBorder(FindColumn(editor.Detail.Layout, "ProductCode")), Is.True, "ProductCode セルに罫線が無い");
            Assert.That(HasCellBorder(FindColumn(editor.Detail.Layout, "Name")), Is.True, "Name セルに罫線が無い");
            Assert.That(HasCellBorder(FindColumn(editor.Detail.Layout, "Price")), Is.False, "Price に罫線が付いている(スコープ超過)");
            Assert.That(HasCellBorder(FindColumn(editor.Detail.Layout, "Stock")), Is.False, "Stock に罫線が付いている(スコープ超過)");
            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<DetailLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.Detail)), "レイアウトが壊れている");
        }

        // 指定フィールドの列を直接含む行を返す(全幅=その行が1列か判定用)。
        static GridRow? RowOf(LayoutDesignBase? layout, string fieldName)
        {
            if (layout is GridLayoutDesign grid)
            {
                foreach (var row in grid.Rows)
                {
                    if (row.Columns.Any(c => c.Layout is FieldLayoutDesign f && f.FieldName == fieldName)) return row;
                    foreach (var col in row.Columns) { var r = RowOf(col.Layout, fieldName); if (r != null) return r; }
                }
            }
            else if (layout is TabLayoutDesign tab)
                foreach (var t in tab.Layouts) { var r = RowOf(t, fieldName); if (r != null) return r; }
            return null;
        }

        static bool HasTabLayout(LayoutDesignBase? layout)
        {
            if (layout is TabLayoutDesign) return true;
            if (layout is GridLayoutDesign grid)
                return grid.Rows.Any(r => r.Columns.Any(c => HasTabLayout(c.Layout)));
            return false;
        }

        [Test]
        public async Task 明細リストを下部に全幅で配置する()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = new List<FieldDesignBase>
            {
                new IdFieldDesign { Name = "Id", DbColumn = "id" },
                new TextFieldDesign { Name = "OrderNo", DbColumn = "order_no", DisplayName = "受注番号" },
                new DateFieldDesign { Name = "OrderDate", DbColumn = "order_date", DisplayName = "受注日" },
                new TextFieldDesign { Name = "Customer", DbColumn = "customer", DisplayName = "顧客" },
                new DetailListFieldDesign { Name = "Details", DisplayName = "明細" },
            };
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage(
                "受注フォームをいい感じに配置してください。受注番号・受注日・顧客は上部にラベル付きで、明細(Details)は下部に全幅で配置して。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0));
            var placed = CollectFieldNames(editor.Detail.Layout);
            foreach (var f in new[] { "OrderNo", "OrderDate", "Customer", "Details" })
                Assert.That(placed, Does.Contain(f), $"{f} が配置されていない");
            var detailsRow = RowOf(editor.Detail.Layout, "Details");
            Assert.That(detailsRow, Is.Not.Null);
            Assert.That(detailsRow!.Columns.Count, Is.EqualTo(1), "明細(Details)が全幅(1列の行)になっていない");
            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<DetailLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.Detail)), "レイアウトが壊れている");
        }

        [Test]
        public async Task 長文テキストを全幅で配置する()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = new List<FieldDesignBase>
            {
                new IdFieldDesign { Name = "Id", DbColumn = "id" },
                new TextFieldDesign { Name = "Title", DbColumn = "title", DisplayName = "件名" },
                new SelectFieldDesign { Name = "Status", DbColumn = "status", Candidates = new() { "未対応,0", "対応中,1" } },
                new TextFieldDesign { Name = "Description", DbColumn = "description", DisplayName = "詳細説明", IsMultiline = true },
            };
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage(
                "いい感じに配置してください。Title と Status は上部に、Description(複数行の詳細説明)は全幅で大きく配置して。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0));
            var placed = CollectFieldNames(editor.Detail.Layout);
            foreach (var f in new[] { "Title", "Status", "Description" })
                Assert.That(placed, Does.Contain(f), $"{f} が配置されていない");
            var descRow = RowOf(editor.Detail.Layout, "Description");
            Assert.That(descRow, Is.Not.Null);
            Assert.That(descRow!.Columns.Count, Is.EqualTo(1), "Description が全幅(1列の行)になっていない");
            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<DetailLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.Detail)), "レイアウトが壊れている");
        }

        [Test]
        public async Task タブで分けて配置する()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = new List<FieldDesignBase>
            {
                new IdFieldDesign { Name = "Id", DbColumn = "id" },
                new TextFieldDesign { Name = "Code", DbColumn = "code", DisplayName = "コード" },
                new TextFieldDesign { Name = "Name", DbColumn = "name", DisplayName = "名称" },
                new TextFieldDesign { Name = "Category", DbColumn = "category", DisplayName = "区分" },
                new NumberFieldDesign { Name = "Price", DbColumn = "price", DisplayName = "売価" },
                new NumberFieldDesign { Name = "Cost", DbColumn = "cost", DisplayName = "原価" },
                new NumberFieldDesign { Name = "Stock", DbColumn = "stock", DisplayName = "在庫" },
            };
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage(
                "基本情報タブ(Code/Name/Category)と価格在庫タブ(Price/Cost/Stock)の2つのタブに分けて配置してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0));
            Assert.That(HasTabLayout(editor.Detail.Layout), Is.True, "TabLayoutDesign が作られていない");
            var placed = CollectFieldNames(editor.Detail.Layout);
            foreach (var f in new[] { "Code", "Name", "Category", "Price", "Cost", "Stock" })
                Assert.That(placed, Does.Contain(f), $"{f} が配置されていない");
            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<DetailLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.Detail)), "レイアウトが壊れている");
        }

        [Test]
        public async Task フィールド追加要求_勝手にデータフィールドを作らず断る()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = new List<FieldDesignBase>
            {
                new IdFieldDesign { Name = "Id", DbColumn = "id" },
                new TextFieldDesign { Name = "ProductCode", DbColumn = "product_code", DisplayName = "商品コード" },
                new TextFieldDesign { Name = "Name", DbColumn = "name", DisplayName = "商品名" },
            };
            var beforeData = fields.Where(f => f is not LabelFieldDesign).Select(f => f.Name).ToHashSet();
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            // レイアウトチャットはデータフィールドを追加できない(ラベルと配置のみ)。これを投げて挙動を見る。
            var reply = await chat.ProcessMessage("メールアドレス(Email)のテキスト入力フィールドを追加してください。");
            TestContext.WriteLine("reply:\n" + reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            // 勝手にデータ(非ラベル)フィールドを作っていない
            var afterData = fields.Where(f => f is not LabelFieldDesign).Select(f => f.Name).ToHashSet();
            Assert.That(afterData, Is.EquivalentTo(beforeData), "存在しないデータフィールドを勝手に追加した(ハルシネーション)");
            // 宙ぶらりん参照を配置していない(配置名は全て既知フィールド/追加ラベルのいずれか)
            var known = fields.Select(f => f.Name).ToHashSet();
            var placed = CollectFieldNames(editor.Detail.Layout);
            Assert.That(placed.All(n => known.Contains(n)), Is.True,
                "存在しないフィールドを配置した: " + string.Join(",", placed.Where(n => !known.Contains(n))));
            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<DetailLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.Detail)), "レイアウトが壊れている");
        }

        [Test]
        public async Task フィールドのプロパティ編集要求_勝手に編集しない()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = new List<FieldDesignBase>
            {
                new IdFieldDesign { Name = "Id", DbColumn = "id" },
                new TextFieldDesign { Name = "ProductCode", DbColumn = "product_code", DisplayName = "商品コード" },
                new NumberFieldDesign { Name = "Price", DbColumn = "price", DisplayName = "単価", Max = null },
            };
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("Price フィールドの最大値(Max)を 100 に設定してください。");
            TestContext.WriteLine("reply:\n" + reply);

            // このレイアウト機能は Layout(配置)しか出さないので、既存フィールドのプロパティは変わらないはず。
            var price = fields.OfType<NumberFieldDesign>().Single(f => f.Name == "Price");
            Assert.That(price.Max, Is.Null, "フィールドのプロパティを勝手に編集した(このチャットでは不可のはず)");
            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<DetailLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.Detail)), "レイアウトが壊れている");
        }

        [Test]
        public async Task 多ターン_罫線を全体に引いてから特定Gridだけに絞る()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = new List<FieldDesignBase>
            {
                new IdFieldDesign { Name = "Id", DbColumn = "id" },
                new TextFieldDesign { Name = "ProductCode", DbColumn = "product_code", DisplayName = "商品コード" },
                new TextFieldDesign { Name = "Name", DbColumn = "name", DisplayName = "商品名" },
                new NumberFieldDesign { Name = "Price", DbColumn = "price", DisplayName = "単価" },
                new NumberFieldDesign { Name = "Stock", DbColumn = "stock", DisplayName = "在庫" },
            };
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            // Turn 1: いったん「全カラムに罫線」を引く(広く適用)
            var r1 = await chat.ProcessMessage(
                "ProductCode, Name, Price, Stock を配置して、すべてのセル(カラム)に罫線(BorderStyle)を引いてください。");
            TestContext.WriteLine("turn1:\n" + r1);

            // Turn 2: 商品コードと商品名のGridだけに絞り、それ以外の罫線は外す(narrow + cleanup)
            var r2 = await chat.ProcessMessage(
                "やっぱり、商品コード(ProductCode)と商品名(Name)を1つのネストGridにまとめて、罫線はその Grid のカラムだけにしてください。" +
                "Price と Stock のカラムの罫線は外してください。");
            TestContext.WriteLine("turn2:\n" + r2);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            var placed = CollectFieldNames(editor.Detail.Layout);
            foreach (var f in new[] { "ProductCode", "Name", "Price", "Stock" })
                Assert.That(placed, Does.Contain(f), $"{f} が配置されていない");

            // 絞り込み後: 対象には罫線が残り、対象外(Price/Stock)からは罫線が外れている(後始末ができている)
            Assert.That(HasCellBorder(FindColumn(editor.Detail.Layout, "ProductCode")), Is.True, "ProductCode の罫線が消えた");
            Assert.That(HasCellBorder(FindColumn(editor.Detail.Layout, "Name")), Is.True, "Name の罫線が消えた");
            Assert.That(HasCellBorder(FindColumn(editor.Detail.Layout, "Price")), Is.False, "Price の罫線が外れていない(後始末失敗)");
            Assert.That(HasCellBorder(FindColumn(editor.Detail.Layout, "Stock")), Is.False, "Stock の罫線が外れていない(後始末失敗)");
            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<DetailLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.Detail)), "レイアウトが壊れている");
        }

        [Test]
        public async Task セクションを枠線カードで分けて配置する()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = new List<FieldDesignBase>
            {
                new IdFieldDesign { Name = "Id", DbColumn = "id" },
                new TextFieldDesign { Name = "Code", DbColumn = "code", DisplayName = "コード" },
                new TextFieldDesign { Name = "Name", DbColumn = "name", DisplayName = "名称" },
                new TextFieldDesign { Name = "Category", DbColumn = "category", DisplayName = "区分" },
                new NumberFieldDesign { Name = "Price", DbColumn = "price", DisplayName = "売価" },
                new NumberFieldDesign { Name = "Cost", DbColumn = "cost", DisplayName = "原価" },
                new NumberFieldDesign { Name = "Stock", DbColumn = "stock", DisplayName = "在庫数" },
                new TextFieldDesign { Name = "Location", DbColumn = "location", DisplayName = "保管場所" },
            };
            // レイアウト機能は既存の見出しラベルを配置するのみ。各入力に対応するラベルを事前投入する。
            foreach (var name in new[] { "Code", "Name", "Category", "Price", "Cost", "Stock", "Location" })
                fields.Add(new LabelFieldDesign { Name = name + "Label", Text = "", RelativeField = name });
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage(
                "詳細画面を3つのセクションに分けて配置してください: 基本情報(Code/Name/Category)、価格情報(Price/Cost)、在庫情報(Stock/Location)。" +
                "各セクションは枠線(IsBordered)のカードで囲み、各入力にラベルを付けてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない");
            var placed = CollectFieldNames(editor.Detail.Layout);
            foreach (var f in new[] { "Code", "Name", "Category", "Price", "Cost", "Stock", "Location" })
                Assert.That(placed, Does.Contain(f), $"{f} が配置されていない");
            Assert.That(CountBorderedGrids(editor.Detail.Layout), Is.GreaterThanOrEqualTo(3),
                "枠線カード(IsBordered の Grid)が3セクション分できていない");
            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<DetailLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.Detail)), "レイアウトが壊れている");
        }

        [Test]
        public async Task 先頭に戻るボタンとタイトルのヘッダ行を置く()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = new List<FieldDesignBase>
            {
                new IdFieldDesign { Name = "Id", DbColumn = "id" },
                new AnchorTagFieldDesign { Name = "BackButton", Target = AnchorTarget.HistoryBack, Icon = "bi bi-arrow-left-circle-fill" },
                new LabelFieldDesign { Name = "PageTitle", Text = "商品詳細", Style = LabelStyle.H4 },
                new TextFieldDesign { Name = "Code", DbColumn = "code", DisplayName = "コード" },
                new TextFieldDesign { Name = "Name", DbColumn = "name", DisplayName = "名称" },
            };
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage(
                "詳細画面の先頭行に 戻るボタン(BackButton)とタイトル(PageTitle)を横に並べ、その下に Code と Name の入力フォームを配置してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない");
            var grid = editor.Detail.Layout as GridLayoutDesign;
            Assert.That(grid, Is.Not.Null);
            Assert.That(grid!.Rows.Count, Is.GreaterThan(0));
            var firstRow = NamesInRow(grid.Rows[0]);
            Assert.That(firstRow, Does.Contain("BackButton"), "先頭行に戻るボタンが無い");
            Assert.That(firstRow, Does.Contain("PageTitle"), "先頭行にタイトルが無い");
            var placed = CollectFieldNames(editor.Detail.Layout);
            Assert.That(placed, Does.Contain("Code"));
            Assert.That(placed, Does.Contain("Name"));
            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<DetailLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.Detail)), "レイアウトが壊れている");
        }

        // 実機の指摘①: BooleanField に見出しラベルを付けてしまう。Boolean は自身が表示名を描画するため見出しラベルは不要。
        [Test]
        public async Task BooleanFieldには見出しラベルを付けない()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = new List<FieldDesignBase>
            {
                new TextFieldDesign { Name = "CustomerCode", DbColumn = "customer_code", DisplayName = "顧客コード" },
                new TextFieldDesign { Name = "CustomerName", DbColumn = "customer_name", DisplayName = "顧客名" },
                new TextFieldDesign { Name = "Email", DbColumn = "email", DisplayName = "メール" },
                new BooleanFieldDesign { Name = "IsActive", DbColumn = "is_active", DisplayName = "有効" },
            };
            // テキスト入力には見出しラベルを事前投入するが、Boolean には付けない(Boolean は自身が表示名を描画するため)。
            foreach (var name in new[] { "CustomerCode", "CustomerName", "Email" })
                fields.Add(new LabelFieldDesign { Name = name + "Label", Text = "", RelativeField = name });
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("フィールドをいい感じに並べてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            var placed = CollectFieldNames(editor.Detail.Layout);
            Assert.That(placed, Does.Contain("IsActive"), "BooleanField が配置されていない");
            // BooleanField を指す見出しラベルは配置されていない(そもそも事前投入していない=作成もされない)。
            var boolLabels = fields.OfType<LabelFieldDesign>()
                .Where(l => l.RelativeField == "IsActive" || l.Text == "有効").ToList();
            Assert.That(boolLabels, Is.Empty,
                "BooleanField に見出しラベルが付いている: " + string.Join(", ", boolLabels.Select(l => l.Name)));
        }

        // 実機の指摘②: レイアウト方式を切り替える(ラベル上→ラベル左/縦並び)とラベルが消える。組み替えでラベルを落とさない。
        [Test]
        public async Task レイアウト方式を切り替えてもラベルが消えない()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = new List<FieldDesignBase>
            {
                new TextFieldDesign { Name = "CustomerCode", DbColumn = "customer_code", DisplayName = "顧客コード" },
                new TextFieldDesign { Name = "CustomerName", DbColumn = "customer_name", DisplayName = "顧客名" },
                new TextFieldDesign { Name = "Email", DbColumn = "email", DisplayName = "メール" },
            };
            // レイアウト機能は既存の見出しラベルを配置するのみ。各入力に対応するラベルを事前投入する。
            foreach (var name in new[] { "CustomerCode", "CustomerName", "Email" })
                fields.Add(new LabelFieldDesign { Name = name + "Label", Text = "", RelativeField = name });
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            // turn1: ラベル上で組む(既存ラベルが配置される)
            var reply1 = await chat.ProcessMessage("フィールドをいい感じに並べてください。ラベルはフィールドの上に置くタイプで。");
            TestContext.WriteLine("=== turn1 ===\n" + reply1);
            var placed1 = CollectFieldNames(editor.Detail.Layout);
            Assert.That(fields.OfType<LabelFieldDesign>().Any(l => placed1.Contains(l.Name)), Is.True, "turn1 でラベルが配置されていない");

            // turn2: 縦並び・1項目1行・ラベル左 に切り替え。ここでラベルを落とさないこと。
            var reply2 = await chat.ProcessMessage("やっぱり縦並びに直して。各行ひとつずつ、ラベルは左側に置いて。");
            TestContext.WriteLine("=== turn2 ===\n" + reply2);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            var placed = CollectFieldNames(editor.Detail.Layout);
            foreach (var f in new[] { "CustomerCode", "CustomerName", "Email" })
                Assert.That(placed, Does.Contain(f), $"{f} が配置されていない");
            // 入力に対応するラベルがレイアウトに残っている(素並びに戻っていない)
            var placedLabelsForInputs = fields.OfType<LabelFieldDesign>()
                .Where(l => placed.Contains(l.Name)
                    && (l.RelativeField is "CustomerCode" or "CustomerName" or "Email"))
                .Select(l => l.RelativeField).Distinct().ToList();
            Assert.That(placedLabelsForInputs.Count, Is.GreaterThanOrEqualTo(3),
                "方式切替後にラベルが消えた(残っているラベル対象: " + string.Join(", ", placedLabelsForInputs) + ")");
        }

        // レイアウト木の全 GridRow を集める(ネストGrid/Tab/Canvas も再帰)。
        static List<GridRow> CollectRows(LayoutDesignBase? layout)
        {
            var rows = new List<GridRow>();
            void Walk(LayoutDesignBase? node)
            {
                switch (node)
                {
                    case GridLayoutDesign grid:
                        foreach (var row in grid.Rows)
                        {
                            rows.Add(row);
                            foreach (var col in row.Columns) Walk(col.Layout);
                        }
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
            return rows;
        }

        // ある行(の直下の列)に置かれた入力フィールド(DbValueFieldDesignBase)の数。
        static int InputCountInRow(GridRow row, Dictionary<string, FieldDesignBase> byName)
            => row.Columns.Count(c => c.Layout is FieldLayoutDesign f
                && !string.IsNullOrEmpty(f.FieldName)
                && byName.TryGetValue(f.FieldName, out var d) && d is DbValueFieldDesignBase);

        // ノード配下(再帰)にある入力フィールド(DbValueFieldDesignBase)の数。ネストGrid/Tab/Canvasも辿る。
        static int InputsUnder(LayoutDesignBase? node, Dictionary<string, FieldDesignBase> byName)
        {
            var n = 0;
            void Walk(LayoutDesignBase? x)
            {
                switch (x)
                {
                    case FieldLayoutDesign f:
                        if (!string.IsNullOrEmpty(f.FieldName)
                            && byName.TryGetValue(f.FieldName, out var d) && d is DbValueFieldDesignBase) n++;
                        break;
                    case GridLayoutDesign g:
                        foreach (var row in g.Rows)
                            foreach (var col in row.Columns) Walk(col.Layout);
                        break;
                    case TabLayoutDesign t:
                        foreach (var l in t.Layouts) Walk(l);
                        break;
                    case CanvasLayoutDesign c:
                        foreach (var e in c.Elements) Walk(e.Layout);
                        break;
                }
            }
            Walk(node);
            return n;
        }

        // 各入力と、それに対応する既存の見出しラベル(Text 空 + RelativeField)を持つマスタ。
        // レイアウト機能は既存ラベルを配置するのみ(作成しない)ため、ラベルは事前投入しておく。
        static List<FieldDesignBase> SimpleMasterFields()
        {
            var fields = new List<FieldDesignBase>
            {
                new TextFieldDesign { Name = "CustomerCode", DbColumn = "customer_code", DisplayName = "顧客コード" },
                new TextFieldDesign { Name = "CustomerName", DbColumn = "customer_name", DisplayName = "顧客名" },
                new TextFieldDesign { Name = "PostalCode", DbColumn = "postal_code", DisplayName = "郵便番号" },
                new TextFieldDesign { Name = "Address", DbColumn = "address", DisplayName = "住所" },
                new TextFieldDesign { Name = "Email", DbColumn = "email", DisplayName = "メール" },
            };
            foreach (var name in new[] { "CustomerCode", "CustomerName", "PostalCode", "Address", "Email" })
                fields.Add(new LabelFieldDesign { Name = name + "Label", Text = "", RelativeField = name });
            return fields;
        }

        // 指定なし「いい感じに並べて」の既定は、1行1項目のラベル左(横に詰めない)。
        [Test]
        public async Task 指定なしの既定は1行1項目のラベル左になる()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = SimpleMasterFields();
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("フィールドをいい感じに並べてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            var byName = fields.Cast<FieldDesignBase>().ToDictionary(f => f.Name, f => f);
            foreach (var l in editor.GetFieldDesigns().OfType<LabelFieldDesign>()) byName[l.Name] = l;

            // どの行も入力は最大1つ(=1行1項目)。2項目以上を横に詰めていない。
            foreach (var row in CollectRows(editor.Detail.Layout))
                Assert.That(InputCountInRow(row, byName), Is.LessThanOrEqualTo(1),
                    "1行に2項目以上が横に並んでいる(既定は1行1項目のはず): " + string.Join(", ", NamesInRow(row)));
            // ラベル左: ラベルが付いている
            var labels = editor.GetFieldDesigns().OfType<LabelFieldDesign>().ToList();
            Assert.That(labels, Is.Not.Empty, "既定なのにラベルが付いていない");

            // 既定はラベル「左」= ラベルと入力が同じ行にある(ラベル上＝別の行 になっていない)。
            var checkedPairs = 0;
            foreach (var label in labels)
            {
                var inputName = !string.IsNullOrEmpty(label.RelativeField) ? label.RelativeField
                    : label.Name.EndsWith("Label") ? label.Name[..^"Label".Length] : null;
                if (string.IsNullOrEmpty(inputName)) continue;
                var labelRow = RowOf(editor.Detail.Layout, label.Name);
                var inputRow = RowOf(editor.Detail.Layout, inputName);
                if (labelRow == null || inputRow == null) continue;
                checkedPairs++;
                Assert.That(ReferenceEquals(labelRow, inputRow), Is.True,
                    $"{label.Name} と {inputName} が別の行(ラベル上)。既定はラベル左＝同じ行にすること");
            }
            Assert.That(checkedPairs, Is.GreaterThan(0), "ラベルと入力の対応が検出できなかった");
        }

        // 幅補正ロジック(NormalizeLabelLeftWidths)の確定的ユニットテスト(AI非依存)。
        // ラベル左の列には文字数推定の幅を統一設定し、ラベル上(別行)のラベル列の幅は外す。
        static FieldLayoutDesign FL(string name) => new() { FieldName = name };
        static GridColumn Col(string name, double? width = null) => new() { Layout = FL(name), Width = width };
        static GridRow RowOfCols(params GridColumn[] cols) { var r = new GridRow(); r.Columns.AddRange(cols); return r; }
        static GridLayoutDesign Grid(params GridRow[] rows) { var g = new GridLayoutDesign(); g.Rows.AddRange(rows); return g; }

        [Test]
        public void NormalizeLabelLeftWidths_ラベル左は幅統一_ラベル上は幅除去()
        {
            var fields = new List<FieldDesignBase>
            {
                new TextFieldDesign { Name = "CustomerCode", DisplayName = "顧客コード" },   // 5文字
                new TextFieldDesign { Name = "Email", DisplayName = "メールアドレス" },        // 7文字(最長)
                // 実運用と同じく Text="" + RelativeField で表示名に追従させる(Text 既定値は "Label")。
                new LabelFieldDesign { Name = "CustomerCodeLabel", Text = "", RelativeField = "CustomerCode" },
                new LabelFieldDesign { Name = "EmailLabel", Text = "", RelativeField = "Email" },
            };

            // ラベル左: [label, input] が同じ行。AIが片方50px・片方nullの不揃いを付けた想定。
            var labelLeft = Grid(
                RowOfCols(Col("CustomerCodeLabel", 50), Col("CustomerCode")),
                RowOfCols(Col("EmailLabel"), Col("Email")));
            DetailLayoutFunction.NormalizeLabelLeftWidths(labelLeft, fields);
            var leftWidths = new[] { labelLeft.Rows[0].Columns[0].Width, labelLeft.Rows[1].Columns[0].Width };
            // 最長「メールアドレス」7文字 → 7*18+40=166px。全ラベル列が同じ値・96以上に統一される。
            Assert.That(leftWidths[0], Is.EqualTo(leftWidths[1]), "ラベル左の幅が揃っていない");
            Assert.That(leftWidths[0], Is.GreaterThanOrEqualTo(96), "ラベル左の幅が狭すぎる");
            Assert.That(leftWidths[0], Is.EqualTo(166), "文字数推定(7*18+40)になっていない");

            // ラベル上: ネストGridで [labelRow][inputRow]。AIが誤って幅130を付けた想定 → 除去される。
            var labelAbove = Grid(
                RowOfCols(new GridColumn
                {
                    Layout = Grid(
                        RowOfCols(Col("CustomerCodeLabel", 130)),
                        RowOfCols(Col("CustomerCode")))
                }));
            DetailLayoutFunction.NormalizeLabelLeftWidths(labelAbove, fields);
            var aboveLabelCol = ((GridLayoutDesign)labelAbove.Rows[0].Columns[0].Layout!).Rows[0].Columns[0];
            Assert.That(aboveLabelCol.Width, Is.Null, "ラベル上のラベル列の幅が除去されていない");
        }

        // ラベル上ブロック(ネストGrid: ラベル行→入力行)をラベル左(同じ行に[ラベル,入力])へ変換する確定的テスト。
        [Test]
        public void ConvertLabelAboveToLabelLeft_ラベル上を同じ行のラベル左にする()
        {
            var fields = new List<FieldDesignBase>
            {
                new TextFieldDesign { Name = "CustomerCode", DisplayName = "顧客コード" },
                new LabelFieldDesign { Name = "CustomerCodeLabel", Text = "", RelativeField = "CustomerCode" },
            };
            // 外側Row → 外側Col → ネストGrid[ ラベル行 / 入力行 ]
            var nested = Grid(
                RowOfCols(Col("CustomerCodeLabel", 130)),
                RowOfCols(Col("CustomerCode")));
            var root = Grid(RowOfCols(new GridColumn { Layout = nested }));

            DetailLayoutFunction.ConvertLabelAboveToLabelLeft(root, fields);

            // ネストGridが単一行[ラベル, 入力]になっている。
            Assert.That(nested.Rows.Count, Is.EqualTo(1), "1行に平坦化されていない");
            var cols = nested.Rows[0].Columns;
            Assert.That(cols.Count, Is.EqualTo(2), "[ラベル, 入力]の2列になっていない");
            Assert.That(((FieldLayoutDesign)cols[0].Layout!).FieldName, Is.EqualTo("CustomerCodeLabel"));
            Assert.That(((FieldLayoutDesign)cols[1].Layout!).FieldName, Is.EqualTo("CustomerCode"));

            // 変換後に幅推定を掛けるとラベル列に幅が付く(ラベル左として成立)。
            DetailLayoutFunction.NormalizeLabelLeftWidths(root, fields);
            Assert.That(cols[0].Width, Is.Not.Null.And.GreaterThanOrEqualTo(96), "変換後のラベル列に推定幅が付いていない");
        }

        // ラベル左のとき、ラベル列の幅が文字数から推定され、狭すぎない(50px 等にならない)・全列同じ幅に揃う。
        // 幅はコード側(NormalizeLabelLeftWidths)で確定設定されるので、ラベル左を明示すれば安定して検証できる。
        [Test]
        public async Task ラベル左のラベル列幅が文字数から推定される()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = SimpleMasterFields();
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("「全体配置-ラベル左」で並べてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            var labelNames = editor.GetFieldDesigns().OfType<LabelFieldDesign>().Select(l => l.Name).ToHashSet();
            // 入力と横並びの(=ラベル左の)ラベル列を集める。
            var labelLeftWidths = new List<double?>();
            foreach (var row in CollectRows(editor.Detail.Layout))
            {
                var hasInput = row.Columns.Any(c => c.Layout is FieldLayoutDesign inf
                    && fields.Any(f => f.Name == inf.FieldName && f is DbValueFieldDesignBase));
                if (!hasInput) continue;
                foreach (var c in row.Columns)
                    if (c.Layout is FieldLayoutDesign lf && labelNames.Contains(lf.FieldName))
                        labelLeftWidths.Add(c.Width);
            }

            Assert.That(labelLeftWidths, Is.Not.Empty, "ラベル左のラベル列が見つからない(ラベル左になっていない)");
            // 狭すぎない & 推定幅が設定されている
            foreach (var w in labelLeftWidths)
                Assert.That(w, Is.Not.Null.And.GreaterThanOrEqualTo(96),
                    $"ラベル列の幅が未設定/狭すぎる({w}px)。文字数から推定した96px以上にすること");
            // 全ラベル列が同じ幅に揃っている
            Assert.That(labelLeftWidths.Distinct().Count(), Is.EqualTo(1),
                "ラベル列の幅が揃っていない: " + string.Join(", ", labelLeftWidths));
        }

        // トップレベル行ごとの「横に並ぶ入力項目数」の最大値(ラベル左でもラベル上ネストブロックでも数える)。
        static int MaxItemsPerTopRow(LayoutDesignBase? layout, Dictionary<string, FieldDesignBase> byName)
        {
            var topRows = (layout as GridLayoutDesign)?.Rows ?? new();
            return topRows.Select(r => r.Columns.Sum(c => InputsUnder(c.Layout, byName))).DefaultIfEmpty(0).Max();
        }

        static Dictionary<string, FieldDesignBase> ByNameWithLabels(List<FieldDesignBase> fields, FakeDetailLayoutEditor editor)
        {
            var byName = fields.ToDictionary(f => f.Name, f => f);
            foreach (var l in editor.GetFieldDesigns().OfType<LabelFieldDesign>()) byName[l.Name] = l;
            return byName;
        }

        // 「全体配置-ラベル左-2」指定なら1行に2項目ずつ詰める(パターン名＋項目数指定が効く)。
        [Test]
        public async Task 全体配置ラベル左2指定で1行2項目に詰める()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = SimpleMasterFields();
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("「全体配置-ラベル左-2」で並べてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            var maxItemsPerRow = MaxItemsPerTopRow(editor.Detail.Layout, ByNameWithLabels(fields, editor));
            Assert.That(maxItemsPerRow, Is.GreaterThanOrEqualTo(2),
                "「全体配置-ラベル左-2」指定なのに1行に2項目並んでいない(最大 " + maxItemsPerRow + " 項目/行)");
        }

        // 「全体配置-ラベル上-3」指定なら1行に3項目ずつ＆ラベルは入力の上(項目数の数字が効く)。
        [Test]
        public async Task 全体配置ラベル上3指定で1行3項目になる()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = SimpleMasterFields(); // 5項目 → 3項目/行 なら [3][2] で最大3
            var editor = new FakeDetailLayoutEditor(fields);
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("「全体配置-ラベル上-3」で並べてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            var maxItemsPerRow = MaxItemsPerTopRow(editor.Detail.Layout, ByNameWithLabels(fields, editor));
            Assert.That(maxItemsPerRow, Is.GreaterThanOrEqualTo(3),
                "「全体配置-ラベル上-3」指定なのに1行に3項目並んでいない(最大 " + maxItemsPerRow + " 項目/行)");
            // ラベル上: ラベルが入力と別の行(上)にある
            var labels = editor.GetFieldDesigns().OfType<LabelFieldDesign>().ToList();
            Assert.That(labels, Is.Not.Empty, "ラベルが付いていない");
            var checkedPairs = 0;
            foreach (var label in labels)
            {
                var inputName = !string.IsNullOrEmpty(label.RelativeField) ? label.RelativeField
                    : label.Name.EndsWith("Label") ? label.Name[..^"Label".Length] : null;
                if (string.IsNullOrEmpty(inputName)) continue;
                var labelRow = RowOf(editor.Detail.Layout, label.Name);
                var inputRow = RowOf(editor.Detail.Layout, inputName);
                if (labelRow == null || inputRow == null) continue;
                checkedPairs++;
                Assert.That(ReferenceEquals(labelRow, inputRow), Is.False,
                    $"{label.Name} と {inputName} が同じ行(ラベル左)。ラベル上では別の行にすること");
            }
            Assert.That(checkedPairs, Is.GreaterThan(0), "ラベルと入力の対応が検出できなかった");
        }

        // EnsureStandardParts の確定的テスト(AI非依存): 既存の標準パーツを、タイトル/戻る=先頭行・サブミット=末尾行に「配置」する(作成はしない)。
        [Test]
        public void EnsureStandardParts_タイトル戻るは先頭_サブミットは末尾に配置()
        {
            // 標準パーツは既にフィールドとして存在する前提(作成は field.create の担当)。
            var fields = new List<FieldDesignBase>
            {
                new TextFieldDesign { Name = "CustomerCode", DisplayName = "顧客コード" },
                new LabelFieldDesign { Name = "CustomerCodeLabel", Text = "", RelativeField = "CustomerCode" },
                new AnchorTagFieldDesign { Name = "BackButton", Target = AnchorTarget.HistoryBack },
                new LabelFieldDesign { Name = "PageTitle", Style = LabelStyle.H4, Text = "顧客マスタ" },
                new SubmitButtonFieldDesign { Name = "SubmitButton" },
            };
            // 本体だけのレイアウト(チョームなし)。標準パーツは未配置の状態から先頭/末尾へ置き直す。
            var root = Grid(RowOfCols(Col("CustomerCodeLabel", 130), Col("CustomerCode")));

            DetailLayoutFunction.EnsureStandardParts(root, fields, placeBack: true, placeTitle: true, placeSubmit: true);

            // 各パーツは(既存として)見つかる。
            var back = fields.OfType<AnchorTagFieldDesign>().FirstOrDefault(a => a.Target == AnchorTarget.HistoryBack || a.Name == "BackButton");
            var title = fields.OfType<LabelFieldDesign>().FirstOrDefault(l => l.Style == LabelStyle.H4);
            var submit = fields.OfType<SubmitButtonFieldDesign>().FirstOrDefault();
            Assert.That(back, Is.Not.Null);
            Assert.That(title, Is.Not.Null);
            Assert.That(submit, Is.Not.Null);

            // 先頭行にタイトルと戻る、末尾行にサブミット。
            var firstRow = NamesUnderRow(root.Rows.First());
            var lastRow = NamesUnderRow(root.Rows.Last());
            Assert.That(firstRow, Does.Contain(back!.Name).And.Contain(title.Name), "先頭行にタイトル/戻るが無い");
            Assert.That(lastRow, Does.Contain(submit!.Name), "末尾行にサブミットが無い");
            // 本体(CustomerCode)も残っている。
            Assert.That(CollectFieldNames(root), Does.Contain("CustomerCode"));
            // 重複配置していない。
            Assert.That(CollectFieldNames(root).Count, Is.EqualTo(new HashSet<string>(
                new[] { back.Name, title.Name, submit.Name, "CustomerCodeLabel", "CustomerCode" }).Count));

            // 戻るボタン: アイコンのみ(TitleText 空)・Style=Text・配置の FontSize=30。
            Assert.That(back.TitleText, Is.Empty, "戻るボタンに文字が残っている(アイコンのみにする)");
            Assert.That(back.Style, Is.EqualTo(AnchorStyle.Text));
            Assert.That(back.Icon, Is.Not.Empty);
            var backCol = root.Rows.First().Columns.First(c => c.Layout is FieldLayoutDesign f && f.FieldName == back.Name);
            Assert.That(((FieldLayoutDesign)backCol.Layout!).FontSize, Is.EqualTo(30), "戻るアイコンの FontSize が30でない");
            // サブミット列は左右中央。
            var submitCol = root.Rows.Last().Columns.First(c => c.Layout is FieldLayoutDesign f && f.FieldName == submit.Name);
            Assert.That(submitCol.HorizontalAlignment, Is.EqualTo(HorizontalAlignment.Center), "サブミット列が中央寄せでない");
        }

        // 既存の戻るボタン(Target=Url・文字が残っている。toolbox 既定の "Anchor Tag" 等)も、
        // icon-only・HistoryBack に正規化して再利用する(重複作成しない)。
        [Test]
        public void EnsureStandardParts_既存BackButtonをiconのみに正規化して再利用する()
        {
            var fields = new List<FieldDesignBase>
            {
                new TextFieldDesign { Name = "CustomerCode", DisplayName = "顧客コード" },
                new AnchorTagFieldDesign
                {
                    Name = "BackButton", Target = AnchorTarget.Url, TitleText = "Anchor Tag",
                    Icon = "bi bi-arrow-left-circle-fill", Style = AnchorStyle.Text,
                },
            };
            var root = Grid(RowOfCols(Col("CustomerCode")));

            DetailLayoutFunction.EnsureStandardParts(root, fields, placeBack: true, placeTitle: false, placeSubmit: false);

            // AnchorTag は1つだけ(重複作成していない)。
            var anchors = fields.OfType<AnchorTagFieldDesign>().ToList();
            Assert.That(anchors.Count, Is.EqualTo(1), "戻るボタンが重複作成された");
            var back = anchors[0];
            Assert.That(back.Name, Is.EqualTo("BackButton"));
            Assert.That(back.Target, Is.EqualTo(AnchorTarget.HistoryBack), "Target が HistoryBack に正規化されていない");
            Assert.That(back.TitleText, Is.Empty, "戻るボタンの文字が消えていない(icon-only でない)");
            // 先頭行に配置されている。
            Assert.That(NamesUnderRow(root.Rows.First()), Does.Contain("BackButton"));
        }

        // 一から全体配置するとき、全セルが空のゴミ行は除去される(EnsureStandardParts は呼ばないが RemoveEmptyRows 経由の確認)。
        [Test]
        public async Task 全体配置で全セル空のゴミ行が残らない()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = SimpleMasterFields();
            var editor = new FakeDetailLayoutEditor(fields, moduleName: "顧客マスタ");
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("フィールドをいい感じに並べてください。");
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            // 全セルが空(配置フィールドが1つも無い)の行が存在しないこと。
            foreach (var row in CollectRows(editor.Detail.Layout))
            {
                var hasContent = row.Columns.Any(c => CollectFieldNames(c.Layout).Count > 0);
                Assert.That(hasContent, Is.True, "全セルが空のゴミ行が残っている");
            }
        }

        // 何も wanted でなければ何もしない(個別編集で勝手にチョームを足さない)。
        [Test]
        public void EnsureStandardParts_何もwantedでなければ変更しない()
        {
            var fields = new List<FieldDesignBase> { new TextFieldDesign { Name = "CustomerCode" } };
            var root = Grid(RowOfCols(Col("CustomerCode")));
            var before = JsonConverterEx.SerializeObject(root);
            DetailLayoutFunction.EnsureStandardParts(root, fields, false, false, false);
            Assert.That(JsonConverterEx.SerializeObject(root), Is.EqualTo(before), "wanted無しなのにレイアウトが変わった");
            Assert.That(fields.Count, Is.EqualTo(1), "wanted無しなのにフィールドが増えた");
        }

        // 行の部分木(ネスト含む)にある全 FieldName。
        static HashSet<string> NamesUnderRow(GridRow row)
        {
            var names = new HashSet<string>();
            foreach (var c in row.Columns)
                foreach (var n in CollectFieldNames(c.Layout)) names.Add(n);
            return names;
        }

        // 何もない状態から全体配置すると、既存のタイトル(上)・戻るボタン(上)・サブミット(下)も慣習位置へ配置される。
        [Test]
        public async Task 全体配置でタイトルと戻るボタンとサブミットを配置する()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = SimpleMasterFields();
            // 標準パーツは既にフィールドとして存在する前提(作成は field.create の担当)。
            fields.Add(new AnchorTagFieldDesign { Name = "BackButton", Target = AnchorTarget.HistoryBack });
            fields.Add(new LabelFieldDesign { Name = "PageTitle", Style = LabelStyle.H4, Text = "顧客マスタ" });
            fields.Add(new SubmitButtonFieldDesign { Name = "SubmitButton" });
            var editor = new FakeDetailLayoutEditor(fields, moduleName: "顧客マスタ");
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("フィールドをいい感じに並べてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            var all = editor.GetFieldDesigns();
            var placed = CollectFieldNames(editor.Detail.Layout);
            var topRows = (editor.Detail.Layout as GridLayoutDesign)!.Rows;
            Assert.That(topRows.Count, Is.GreaterThan(0));
            var firstRow = NamesUnderRow(topRows.First());
            var lastRow = NamesUnderRow(topRows.Last());

            // 戻るボタン(Target=HistoryBack)が一番上の行に配置される。
            var back = all.OfType<AnchorTagFieldDesign>().FirstOrDefault(a => a.Target == AnchorTarget.HistoryBack || a.Name == "BackButton");
            Assert.That(back, Is.Not.Null, "戻るボタンが無い");
            Assert.That(firstRow, Does.Contain(back!.Name), "戻るボタンが一番上の行に無い");

            // タイトル(H4 ラベル)が一番上の行に配置される。
            var title = all.OfType<LabelFieldDesign>().FirstOrDefault(l => l.Style == LabelStyle.H4);
            Assert.That(title, Is.Not.Null, "タイトル(H4)が無い");
            Assert.That(firstRow, Does.Contain(title!.Name), "タイトルが一番上の行に無い");

            // サブミットボタンが一番下の行に配置される。
            var submit = all.OfType<SubmitButtonFieldDesign>().FirstOrDefault();
            Assert.That(submit, Is.Not.Null, "サブミットボタンが無い");
            Assert.That(lastRow, Does.Contain(submit!.Name), "サブミットボタンが一番下の行に無い");

            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<DetailLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.Detail)), "レイアウトが壊れている");
        }

        // 明示的にサブミットボタンの配置を頼んだら、既存のサブミットボタンを配置する(以前は断っていた)。
        [Test]
        public async Task サブミットボタンの配置依頼で配置する()
        {
            var settings = TestEnv.RequireChatClientFactory();
            // 既にラベル左フォームがある状態(空からの全体配置ではない)。サブミットボタンは既存フィールドとして存在する。
            var fields = new List<FieldDesignBase>
            {
                new TextFieldDesign { Name = "CustomerCode", DisplayName = "顧客コード" },
                new TextFieldDesign { Name = "CustomerName", DisplayName = "顧客名" },
                new LabelFieldDesign { Name = "CustomerCodeLabel", Text = "", RelativeField = "CustomerCode" },
                new LabelFieldDesign { Name = "CustomerNameLabel", Text = "", RelativeField = "CustomerName" },
                new SubmitButtonFieldDesign { Name = "SubmitButton" },
            };
            var initial = Grid(
                RowOfCols(Col("CustomerCodeLabel", 130), Col("CustomerCode")),
                RowOfCols(Col("CustomerNameLabel", 130), Col("CustomerName")));
            var editor = new FakeDetailLayoutEditor(fields, initial, "顧客マスタ");
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("サブミットボタンを追加してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            var submit = editor.GetFieldDesigns().OfType<SubmitButtonFieldDesign>().FirstOrDefault();
            Assert.That(submit, Is.Not.Null, "サブミットボタンが無い");
            Assert.That(CollectFieldNames(editor.Detail.Layout), Does.Contain(submit!.Name), "サブミットボタンが配置されていない(断ってしまった)");
        }

        // 個別編集では、頼まれていないタイトル/戻る/サブミットを勝手に(既存フィールドであっても)配置し直さない。
        [Test]
        public async Task 個別編集で標準パーツを勝手に配置し直さない()
        {
            var settings = TestEnv.RequireChatClientFactory();
            // 標準パーツは既存フィールドとして存在するが、まだレイアウトには配置していない状態。
            var fields = new List<FieldDesignBase>
            {
                new TextFieldDesign { Name = "CustomerCode", DisplayName = "顧客コード" },
                new TextFieldDesign { Name = "CustomerName", DisplayName = "顧客名" },
                new LabelFieldDesign { Name = "CustomerCodeLabel", Text = "", RelativeField = "CustomerCode" },
                new LabelFieldDesign { Name = "CustomerNameLabel", Text = "", RelativeField = "CustomerName" },
                new AnchorTagFieldDesign { Name = "BackButton", Target = AnchorTarget.HistoryBack },
                new LabelFieldDesign { Name = "PageTitle", Style = LabelStyle.H4, Text = "顧客マスタ" },
                new SubmitButtonFieldDesign { Name = "SubmitButton" },
            };
            var initial = Grid(
                RowOfCols(Col("CustomerCodeLabel", 130), Col("CustomerCode")),
                RowOfCols(Col("CustomerNameLabel", 130), Col("CustomerName")));
            var editor = new FakeDetailLayoutEditor(fields, initial, "顧客マスタ");
            var chat = new DetailLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("顧客名の行を一番上に移動してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.Detail.Layout));

            // 個別編集(行の移動)では、頼んでいない標準パーツをレイアウトに配置しない。
            var placed = CollectFieldNames(editor.Detail.Layout);
            Assert.That(placed, Does.Not.Contain("SubmitButton"), "頼んでいないサブミットボタンが配置された");
            Assert.That(placed, Does.Not.Contain("BackButton"), "頼んでいない戻るボタンが配置された");
            Assert.That(placed, Does.Not.Contain("PageTitle"), "頼んでいないタイトルが配置された");
        }
    }
}

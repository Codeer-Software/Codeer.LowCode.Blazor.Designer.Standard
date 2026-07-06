using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// ListLayoutFunction の検証。
    /// 一覧はフラットな行×列テーブル。詳細/検索Chatで得た知見の反映を主眼に検証する:
    /// ①「いい感じに列を並べて」で主要フィールドが列になる ②Id 等システム列は明示要求が無ければ出さない
    /// ③名指しされたら Id も出す ④空応答で既存の列を消さない。
    /// 実 AI を呼ぶため、AZURE_OPENAI_* 未設定時は Ignore される。
    /// </summary>
    [TestFixture]
    public class ListLayoutTest
    {
        static List<string> CollectColumnFieldNames(ListLayoutDesign list)
            => list.Elements.SelectMany(row => row)
                .Where(c => !string.IsNullOrEmpty(c.FieldName))
                .Select(c => c.FieldName).ToList();

        static List<FieldDesignBase> CustomerFields() => new()
        {
            new IdFieldDesign { Name = "Id", DbColumn = "id" },
            new TextFieldDesign { Name = "CustomerCode", DbColumn = "customer_code", DisplayName = "顧客コード" },
            new TextFieldDesign { Name = "CustomerName", DbColumn = "customer_name", DisplayName = "顧客名" },
            new SelectFieldDesign { Name = "Status", DbColumn = "status", DisplayName = "ステータス" },
            new TextFieldDesign { Name = "PostalCode", DbColumn = "postal_code", DisplayName = "郵便番号" },
            new TextFieldDesign { Name = "PhoneNumber", DbColumn = "phone_number", DisplayName = "電話番号" },
            new TextFieldDesign { Name = "Memo", DbColumn = "memo", DisplayName = "メモ", IsMultiline = true },
            new DateTimeFieldDesign { Name = "CreatedAt", DbColumn = "created_at" },
        };

        [Test]
        public async Task いい感じに列を並べる_主要列が並びIdは出さない()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = CustomerFields();
            var editor = new FakeListLayoutEditor(fields);
            var chat = new ListLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("一覧の列をいい感じに並べてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.List.Elements));

            var cols = CollectColumnFieldNames(editor.List);
            foreach (var f in new[] { "CustomerCode", "CustomerName", "Status" })
                Assert.That(cols, Does.Contain(f), $"{f} が列に無い");
            // Id は明示要求していないので列に出さない。
            Assert.That(cols, Does.Not.Contain("Id"), "Id 列を出している");
            // 空セル(FieldName/DetailLayout/Component 全部空)を作らない。
            Assert.That(editor.List.Elements.SelectMany(r => r).All(c =>
                    !string.IsNullOrEmpty(c.FieldName) || !string.IsNullOrEmpty(c.DetailLayoutName) || !string.IsNullOrEmpty(c.ListElementComponent)),
                Is.True, "空セルが含まれている");

            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<ListLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.List)), "レイアウトが壊れている");
        }

        [Test]
        public async Task Idを明示要求したら列に出す()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = CustomerFields();
            var editor = new FakeListLayoutEditor(fields);
            var chat = new ListLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("Id・顧客コード・顧客名の列を並べてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.List.Elements));

            var cols = CollectColumnFieldNames(editor.List);
            Assert.That(cols, Does.Contain("Id"), "明示要求した Id が列に出ていない");
            Assert.That(cols, Does.Contain("CustomerCode"));
            Assert.That(cols, Does.Contain("CustomerName"));
        }

        // 行番号要求の語彙認識(AI不要の決定的テスト)。「No」「番号」も ListNo と理解する。
        [Test]
        [TestCase("No追加", true)]
        [TestCase("Noも追加", true)]
        [TestCase("番号を追加して", true)]
        [TestCase("ListNo追加", true)]
        [TestCase("行番号を先頭に", true)]
        [TestCase("連番をつけて", true)]
        [TestCase("電話番号を追加して", false)]   // 既存フィールド(電話番号)の指定なので行番号扱いにしない
        [TestCase("郵便番号の列を出して", false)]  // 同上(郵便番号)
        [TestCase("顧客名の幅を200に", false)]
        public void 行番号要求の語彙認識(string message, bool expected)
        {
            var fields = CustomerFields();
            Assert.That(ListLayoutFunction.ListNumberRequested(message, fields), Is.EqualTo(expected), $"'{message}' の判定が誤り");
        }

        // 二段で列数合わせの空セル(スペーサー)は許可される(矩形なら OK)。
        [Test]
        public void 二段で空セルスペーサーは矩形なら許可()
        {
            var elements = new List<List<ListElement>>
            {
                new()
                {
                    new ListElement { FieldName = "CustomerCode" },
                    new ListElement { FieldName = "CustomerName" },
                    new ListElement { FieldName = "" },  // スペーサー
                },
                new()
                {
                    new ListElement { FieldName = "PostalCode" },
                    new ListElement { FieldName = "Address" },
                    new ListElement { FieldName = "PhoneNumber" },
                },
            };
            Assert.That(ListLayoutFunction.GetGridConsistencyError(elements), Is.Null, "矩形(スペーサー込み)なのに誤検出している");
        }

        // 不揃いな二段をツールが自動で矩形化する(AI不要の決定的テスト)。
        [Test]
        public void 不揃いな二段をツールが矩形化する()
        {
            // 上段3・下段5でバラバラ(=例外の元)。ツールが矩形化すると隙間なしになる。
            var elements = new List<List<ListElement>>
            {
                new()
                {
                    new ListElement { FieldName = "CustomerCode" },
                    new ListElement { FieldName = "CustomerName" },
                    new ListElement { FieldName = "CustomerKana" },
                },
                new()
                {
                    new ListElement { FieldName = "PostalCode" },
                    new ListElement { FieldName = "Address" },
                    new ListElement { FieldName = "PhoneNumber" },
                    new ListElement { FieldName = "Email" },
                    new ListElement { FieldName = "IsActive" },
                },
            };
            ListLayoutFunction.RectangularizeMultiRowGrid(elements);

            // 矩形化後はグリッド整合エラーが無い。
            Assert.That(ListLayoutFunction.GetGridConsistencyError(elements), Is.Null, "矩形化できていない");
            // 全ての元フィールドは保持される。
            var placed = elements.SelectMany(r => r).Where(c => !string.IsNullOrEmpty(c.FieldName)).Select(c => c.FieldName).ToList();
            foreach (var f in new[] { "CustomerCode", "CustomerName", "CustomerKana", "PostalCode", "Address", "PhoneNumber", "Email", "IsActive" })
                Assert.That(placed, Does.Contain(f), $"{f} が失われた");
            // 空セル(スペーサー)は作らない。
            Assert.That(elements.SelectMany(r => r).Any(c => string.IsNullOrEmpty(c.FieldName)), Is.False, "空セルが作られている");
            // 結合指定が無いので各段へ均等に再配分される(3/5 → 4/4)。2段目だけ列数が少ない、を避ける。
            Assert.That(elements[0].Count, Is.EqualTo(4), "上段の列数が均等でない");
            Assert.That(elements[1].Count, Is.EqualTo(4), "下段の列数が均等でない");
            Assert.That(elements[0].Sum(c => System.Math.Max(1, c.ColumnSpan)), Is.EqualTo(4));
            Assert.That(elements[1].Sum(c => System.Math.Max(1, c.ColumnSpan)), Is.EqualTo(4));
        }

        // 結合(ColumnSpan>1)の明示指定があるときは、段への再配分をせず行の割り当てを尊重する。
        [Test]
        public void 結合指定があるときは再配分しない()
        {
            var elements = new List<List<ListElement>>
            {
                new()
                {
                    new ListElement { FieldName = "CustomerCode" },
                    new ListElement { FieldName = "CustomerName" },
                },
                new()
                {
                    new ListElement { FieldName = "PostalCode" },
                    new ListElement { FieldName = "Address", ColumnSpan = 2 },  // 明示結合
                    new ListElement { FieldName = "PhoneNumber" },
                },
            };
            ListLayoutFunction.RectangularizeMultiRowGrid(elements);

            Assert.That(ListLayoutFunction.GetGridConsistencyError(elements), Is.Null, "矩形化できていない");
            // 再配分されず、住所は2段目のまま・ColumnSpan=2 が保持される。
            Assert.That(elements[1].Any(c => c.FieldName == "Address" && c.ColumnSpan == 2), Is.True, "住所の結合指定が失われた");
            Assert.That(elements[0].Any(c => c.FieldName == "CustomerCode"), Is.True, "上段の割り当てが変わった");
        }

        // グリッド矩形チェック(AI不要の決定的テスト)。顧客マスタで例外が出た実データ構造を再現する。
        [Test]
        public void 二段で列数が揃わないグリッドはエラーになる()
        {
            // 実際に例外が出た形: Row0=4セル(ListNo は RowSpan=2), Row1=5セル。ListNo の縦マージ込みで列数が合わない。
            var elements = new List<List<ListElement>>
            {
                new()
                {
                    new ListElement { FieldName = "ListNo", RowSpan = 2 },
                    new ListElement { FieldName = "CustomerCode" },
                    new ListElement { FieldName = "CustomerName" },
                    new ListElement { FieldName = "CustomerKana" },
                },
                new()
                {
                    new ListElement { FieldName = "PostalCode" },
                    new ListElement { FieldName = "Address" },
                    new ListElement { FieldName = "PhoneNumber" },
                    new ListElement { FieldName = "Email" },
                    new ListElement { FieldName = "IsActive" },
                },
            };
            Assert.That(ListLayoutFunction.GetGridConsistencyError(elements), Is.Not.Null, "列数不揃いを検出できていない");
        }

        [Test]
        public void 二段でも矩形ならエラーにならない()
        {
            // 総列数=5 に統一(ListNo が両段を縦マージ + 各段で ColumnSpan 調整)。
            var elements = new List<List<ListElement>>
            {
                new()
                {
                    new ListElement { FieldName = "ListNo", RowSpan = 2 },
                    new ListElement { FieldName = "CustomerCode", ColumnSpan = 2 },
                    new ListElement { FieldName = "CustomerName", ColumnSpan = 2 },
                },
                new()
                {
                    new ListElement { FieldName = "PostalCode" },
                    new ListElement { FieldName = "Address" },
                    new ListElement { FieldName = "PhoneNumber" },
                    new ListElement { FieldName = "Email" },
                },
            };
            Assert.That(ListLayoutFunction.GetGridConsistencyError(elements), Is.Null, "矩形なのに誤検出している");
        }

        [Test]
        public void 単一行はグリッドチェック対象外()
        {
            var elements = new List<List<ListElement>>
            {
                new() { new ListElement { FieldName = "A" }, new ListElement { FieldName = "B" }, new ListElement { FieldName = "C" } }
            };
            Assert.That(ListLayoutFunction.GetGridConsistencyError(elements), Is.Null);
        }

        [Test]
        public async Task 行番号列をツールが追加する_既定は左端()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = CustomerFields();
            // レイアウト機能は既存フィールドを配置するのみ(作成は field.create の担当)。行番号フィールドを事前投入する。
            fields.Add(new ListNumberFieldDesign { Name = "ListNo" });
            var initial = new ListLayoutDesign
            {
                Elements = new()
                {
                    new() { new ListElement { FieldName = "CustomerCode" }, new ListElement { FieldName = "CustomerName" } }
                }
            };
            var editor = new FakeListLayoutEditor(fields, initial);
            var chat = new ListLayoutFunction(settings, editor);

            var reply = await chat.ProcessMessage("行番号の列を追加してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.List.Elements));

            // 既存の ListNumberField がツールにより配置される(作成はしない)。
            var listNumber = fields.OfType<ListNumberFieldDesign>().FirstOrDefault();
            Assert.That(listNumber, Is.Not.Null, "ListNumberField(既存)が見つからない");

            // 既定は左端(先頭列)に1つだけ配置。
            var firstRow = editor.List.Elements[0];
            Assert.That(firstRow[0].FieldName, Is.EqualTo(listNumber!.Name), "行番号列が左端に無い");
            var count = editor.List.Elements.SelectMany(r => r).Count(c => c.FieldName == listNumber.Name);
            Assert.That(count, Is.EqualTo(1), "行番号列が重複している");
            // 既存の列は残っている。
            Assert.That(CollectColumnFieldNames(editor.List), Does.Contain("CustomerCode"));
        }

        [Test]
        public async Task 二段にして_行番号は縦マージ左端_空セル無しで適用される()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = CustomerFields();
            fields.Add(new ListNumberFieldDesign { Name = "ListNo" });
            // 既に行番号列(ListNo)を持つ単一行の一覧。
            var initial = new ListLayoutDesign
            {
                Elements = new()
                {
                    new()
                    {
                        new ListElement { FieldName = "ListNo", Width = 60 },
                        new ListElement { FieldName = "CustomerCode" },
                        new ListElement { FieldName = "CustomerName" },
                        new ListElement { FieldName = "PostalCode" },
                        new ListElement { FieldName = "Address" },
                        new ListElement { FieldName = "PhoneNumber" },
                    }
                }
            };
            var editor = new FakeListLayoutEditor(fields, initial);
            var chat = new ListLayoutFunction(settings, editor);

            // 追加の細かい指示(縦マージ・空セル解消・横マージ)なしで「二段にして」だけ。
            var reply = await chat.ProcessMessage("二段にして");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.List.Elements));

            var els = editor.List.Elements;
            Assert.That(els.Count, Is.GreaterThanOrEqualTo(2), "二段になっていない");
            // 矩形(描画例外にならない)。
            Assert.That(ListLayoutFunction.GetGridConsistencyError(els), Is.Null, "矩形になっていない");
            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない");

            // 行番号(ListNo)は左端(先頭)かつ全段を縦マージ(RowSpan=段数)。
            var head = els[0][0];
            Assert.That(head.FieldName, Is.EqualTo("ListNo"), "行番号が左端に無い");
            Assert.That(head.RowSpan, Is.EqualTo(els.Count), "行番号が縦マージされていない");
            // 行番号は1つだけ(重複配置なし)。
            Assert.That(els.SelectMany(r => r).Count(c => c.FieldName == "ListNo"), Is.EqualTo(1), "行番号が重複している");
            // 空セル(スペーサー)を作らず、ColumnSpan で埋めている。
            Assert.That(els.SelectMany(r => r).Any(c => !IsMeaningful(c)), Is.False, "空セルが作られている");

            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<ListLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.List)), "レイアウトが壊れている");
        }

        static bool IsMeaningful(ListElement c)
            => !string.IsNullOrEmpty(c.FieldName) || !string.IsNullOrEmpty(c.DetailLayoutName) || !string.IsNullOrEmpty(c.ListElementComponent);

        // 顧客マスタで壊れた2ステップ(二段にして → 顧客コード1セル/住所2セルマージ)を再現し、常に矩形・空セル無しで適用されることを確認。
        [Test]
        public async Task 二段のあと個別のセル幅調整をしても壊れない()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = CustomerFields();
            fields.Add(new ListNumberFieldDesign { Name = "ListNo" });
            fields.Add(new TextFieldDesign { Name = "Address", DbColumn = "address", DisplayName = "住所" });
            var initial = new ListLayoutDesign
            {
                Elements = new()
                {
                    new()
                    {
                        new ListElement { FieldName = "ListNo", Width = 60 },
                        new ListElement { FieldName = "CustomerCode" },
                        new ListElement { FieldName = "CustomerName" },
                        new ListElement { FieldName = "PostalCode" },
                        new ListElement { FieldName = "Address" },
                        new ListElement { FieldName = "PhoneNumber" },
                    }
                }
            };
            var editor = new FakeListLayoutEditor(fields, initial);
            var chat = new ListLayoutFunction(settings, editor);

            var r1 = await chat.ProcessMessage("二段にして");
            TestContext.WriteLine("① " + r1);
            var r2 = await chat.ProcessMessage("顧客コードは一セルにして、住所を2セル分に横結合して");
            TestContext.WriteLine("② " + r2);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(editor.List.Elements));

            var els = editor.List.Elements;
            // 常に矩形・空セル無し(壊れない)。
            Assert.That(ListLayoutFunction.GetGridConsistencyError(els), Is.Null, "矩形になっていない(壊れている)");
            Assert.That(els.SelectMany(r => r).Any(c => !IsMeaningful(c)), Is.False, "空セルが残っている");
            // 行番号は左端・縦マージ・重複なし。
            Assert.That(els[0][0].FieldName, Is.EqualTo("ListNo"));
            Assert.That(els[0][0].RowSpan, Is.EqualTo(els.Count));
            Assert.That(els.SelectMany(r => r).Count(c => c.FieldName == "ListNo"), Is.EqualTo(1));
            // デシリアライズできる。
            Assert.DoesNotThrow(() => JsonConverterEx.DeserializeObject<ListLayoutDesign>(
                JsonConverterEx.SerializeObject(editor.List)));
        }

        [Test]
        public async Task 空応答で既存の列を消さない()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var fields = CustomerFields();
            // 既存の列(顧客コード・顧客名)を持つ一覧。
            var initial = new ListLayoutDesign
            {
                Elements = new()
                {
                    new() { new ListElement { FieldName = "CustomerCode" }, new ListElement { FieldName = "CustomerName" } }
                }
            };
            var editor = new FakeListLayoutEditor(fields, initial);
            var chat = new ListLayoutFunction(settings, editor);

            // 列に出せない依頼(フィールドのプロパティ編集)→ 断られ、既存の列は保持される。
            var reply = await chat.ProcessMessage("顧客名フィールドを必須にしてください。");
            TestContext.WriteLine(reply);

            var cols = CollectColumnFieldNames(editor.List);
            Assert.That(cols, Does.Contain("CustomerCode"), "既存の列が消えている");
            Assert.That(cols, Does.Contain("CustomerName"), "既存の列が消えている");
        }
    }
}

using Microsoft.Extensions.AI;
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
    /// 各フィールド型を AI が正しく「作成」できるかの網羅テスト。
    /// 1セッションで複数型をまとめて追加させ、期待する FieldDesign 型が出来ているかを型単位で検証する。
    /// (型違い・未作成があれば、その型名が失敗メッセージに出る → ドキュメント改善のループに乗せる)
    /// </summary>
    [TestFixture]
    public class FieldTypeCoverageTest
    {
        // 編集対象モジュール(Sample) + 参照先(Category) を持つチャットを作る。
        static (FieldCreateFunction Chat, ModuleDesign Module) NewChat(Func<IChatClient> settings)
        {
            var module = new ModuleDesign { Name = "Sample", DataSourceName = "Main", DbTable = "sample" };
            module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });

            var category = new ModuleDesign { Name = "Category", DataSourceName = "Main", DbTable = "category" };
            category.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            category.Fields.Add(new TextFieldDesign { Name = "CategoryName", DbColumn = "category_name" });

            var designData = new DesignData { Modules = new FakeModuleDesigns(new[] { module, category }) };
            var dataSources = new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } };
            var host = new FakeChatHost(designData, dataSources);
            return (new FieldCreateFunction(host, settings, AiFunctionFactory.ToFieldContext(new FakeOverallSettingsEditor(module))!), module);
        }

        static void AssertTypes(ModuleDesign module, params Type[] expected)
        {
            var present = module.Fields.Select(f => f.GetType()).ToHashSet();
            var missing = expected.Where(t => !present.Contains(t)).Select(t => t.Name).ToList();
            TestContext.WriteLine("=== 作成された型 ===\n" + string.Join("\n", module.Fields.Select(f => $"{f.Name}: {f.GetType().Name}")));
            Assert.That(missing, Is.Empty, "未作成または型違いのフィールド型: " + string.Join(", ", missing));
        }

        [Test]
        public async Task 基本入力フィールド型を一括作成()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (chat, module) = NewChat(settings);

            var reply = await chat.ProcessMessage(
                "次の型のフィールドを1つずつ追加してください(各 DB カラム名 snake_case も付ける): " +
                "テキスト(TextField)、数値(NumberField・小数2桁)、真偽(BooleanField)、日付(DateField)、" +
                "日時(DateTimeField)、時刻(TimeField)、パスワード(PasswordField)。");
            TestContext.WriteLine(reply);

            AssertTypes(module,
                typeof(TextFieldDesign), typeof(NumberFieldDesign), typeof(BooleanFieldDesign),
                typeof(DateFieldDesign), typeof(DateTimeFieldDesign), typeof(TimeFieldDesign),
                typeof(PasswordFieldDesign));
        }

        [Test]
        public async Task 選択_参照_表示_ボタン系のフィールド型を一括作成()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (chat, module) = NewChat(settings);

            var reply = await chat.ProcessMessage(
                "次の型のフィールドを1つずつ追加してください: " +
                "ドロップダウン(SelectField・候補は 通常/特価)、ラジオグループ(RadioGroupField)とその選択肢(RadioButtonField を2つ)、" +
                "他モジュール参照のリンク(LinkField・参照先は Category モジュール)、" +
                "ラベル(LabelField)、HTML表示(MarkupStringField)、リンク/遷移(AnchorTagField)、画像表示(ImageViewerField)、" +
                "汎用ボタン(ButtonField)、保存ボタン(SubmitButtonField)、コピー(CopyModuleButtonField)、表示編集切替(ViewEditToggleButtonField)、" +
                "ファイル(FileField)、JSON(JsonField)。DBカラムが要るものは snake_case の名前を付けてください。");
            TestContext.WriteLine(reply);

            AssertTypes(module,
                typeof(SelectFieldDesign), typeof(RadioGroupFieldDesign), typeof(RadioButtonFieldDesign),
                typeof(LinkFieldDesign), typeof(LabelFieldDesign), typeof(MarkupStringFieldDesign),
                typeof(AnchorTagFieldDesign), typeof(ImageViewerFieldDesign),
                typeof(ButtonFieldDesign), typeof(SubmitButtonFieldDesign), typeof(CopyModuleButtonFieldDesign),
                typeof(ViewEditToggleButtonFieldDesign), typeof(FileFieldDesign), typeof(JsonFieldDesign));
        }

        [Test]
        public async Task リスト_構造_メニュー系のフィールド型を一括作成()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (chat, module) = NewChat(settings);

            var reply = await chat.ProcessMessage(
                "次の型のフィールドを1つずつ追加してください(参照先や子に他モジュールが要るものは Category モジュールを使う): " +
                "一覧表示(ListField で Category を表示)、明細インライン編集(DetailListField で Category)、タイル一覧(TileListField で Category)、" +
                "他モジュール埋め込み(ModuleField で Category)、検索フォーム(SearchField で Category を検索)、" +
                "行番号(ListNumberField)、ページング(ListPagingField)、" +
                "ヘッダメニュー(HeaderMenuField)、サイドバーメニュー(SidebarMenuField)、コンテキストメニュー(ContextMenuField)。");
            TestContext.WriteLine(reply);

            AssertTypes(module,
                typeof(ListFieldDesign), typeof(DetailListFieldDesign), typeof(TileListFieldDesign),
                typeof(ModuleFieldDesign), typeof(SearchFieldDesign),
                typeof(ListNumberFieldDesign), typeof(ListPagingFieldDesign),
                typeof(HeaderMenuFieldDesign), typeof(SidebarMenuFieldDesign), typeof(ContextMenuFieldDesign));
        }
    }
}

using Microsoft.Extensions.AI;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// OverallSettingsChat を実際の Azure OpenAI に対して動かす統合テスト。
    /// プロンプト/検証ロジックの品質を、AI の実出力を見ながら改善するためのループ用。
    /// API キー等は環境変数 AZURE_OPENAI_API_ENDPOINT / _KEY / _MODEL から取得する。
    /// 未設定なら Ignore（CI 等で勝手に失敗しないように）。
    /// </summary>
    [TestFixture]
    public class OverallSettingsChatTest
    {
        // 商品マスタ相当の小さなモジュール(Id + コード + 名前)を作る。
        static ModuleDesign BuildSampleModule()
        {
            var m = new ModuleDesign { Name = "ProductMaster", DataSourceName = "Main", DbTable = "product_master" };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "ProductCode", DbColumn = "product_code" });
            m.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            return m;
        }

        static (FieldCreateFunction Chat, ModuleDesign Module, FakeOverallSettingsEditor Editor, FakeChatHost Host) CreateChat(Func<IChatClient> settings)
        {
            var module = BuildSampleModule();
            var designData = new DesignData { Modules = new FakeModuleDesigns(new[] { module }) };
            var dataSources = new List<DataSource>
            {
                new DataSource { Name = "Main", DataSourceType = DataSourceType.SQLite }
            };
            var host = new FakeChatHost(designData, dataSources);
            var editor = new FakeOverallSettingsEditor(module);
            return (new FieldCreateFunction(host, settings, AiFunctionFactory.ToFieldContext(editor)!), module, editor, host);
        }

        [Test]
        public async Task 楽観ロック_アプリインクリメント_DbカラムをセットするＡＩ()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (chat, module, editor, _) = CreateChat(settings);

            var reply = await chat.ProcessMessage(
                "楽観ロックのフィールドを追加して。アプリ(ソフト)でバージョンをインクリメントする方式にして、DBのカラム名も割り付けて。");

            TestContext.WriteLine("=== AI reply ===");
            TestContext.WriteLine(reply);
            TestContext.WriteLine("=== module after ===");
            TestContext.WriteLine(JsonConverterEx.SerializeObject(module));

            var opt = module.Fields.OfType<OptimisticLockingFieldDesign>().FirstOrDefault();
            Assert.That(opt, Is.Not.Null, "OptimisticLockingFieldDesign が追加されていない");
            Assert.That(opt!.IncrementVersion, Is.True, "IncrementVersion が true でない（アプリインクリメント方式になっていない）");
            Assert.That(opt.DbColumn, Is.Not.Empty, "DbColumn が空（カラム名が割り付いていない）");
            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "Update が呼ばれていない（適用されていない）");
        }

        [Test]
        public async Task テキスト項目を追加_フィールドが増える()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (chat, module, _, _) = CreateChat(settings);
            var before = module.Fields.Count;

            var reply = await chat.ProcessMessage("備考(メモ)のテキスト項目を1つ追加して。DBカラム名も付けて。");

            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(module));

            Assert.That(module.Fields.Count, Is.GreaterThan(before), "フィールドが追加されていない");
            var added = module.Fields.OfType<TextFieldDesign>()
                .FirstOrDefault(f => f.Name != "ProductCode" && f.Name != "Name");
            Assert.That(added, Is.Not.Null, "新しい TextFieldDesign が見当たらない");
            Assert.That(added!.DbColumn, Is.Not.Empty, "追加項目に DbColumn が付いていない");
        }

        // モジュール設定編集で「テーブル名を入れて」→ Id 主キーが未整備でもテーブル名は適用される回帰テスト。
        // CheckIdExistsForCUD は DbTable+DataSource が揃うと発火する(=名前を入れた瞬間に新規エラー化)ため、
        // 「既存エラーは無視」では直らない。主キー助言は非ブロックとして適用し、注意として伝える。
        [Test]
        public async Task テーブル名設定はId主キーが無くても適用され助言を添える()
        {
            var settings = TestEnv.RequireChatClientFactory();
            // Id を持たない CUD モジュール(DbTable は未設定)。
            var module = new ModuleDesign { Name = "商品マスタ", DataSourceName = "Main", DbTable = "" };
            module.Fields.Add(new TextFieldDesign { Name = "ProductCode", DbColumn = "product_code" });
            module.Fields.Add(new TextFieldDesign { Name = "ProductName", DbColumn = "product_name" });

            var designData = new DesignData { Modules = new FakeModuleDesigns(new[] { module }) };
            var dataSources = new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } };
            var host = new FakeChatHost(designData, dataSources);
            var editor = new FakeOverallSettingsEditor(module);
            // 実コア CheckIdExistsForCUD と同じ発火条件・同じ Location(Member = モジュール名) を再現する。
            editor.CheckOverride = m =>
            {
                var errs = new List<DesignCheckInfo>();
                if (!string.IsNullOrEmpty(m.DbTable) && !string.IsNullOrEmpty(m.DataSourceName)
                    && (m.CanCreate || m.CanUpdate || m.CanDelete)
                    && !m.Fields.Any(f => f.Name == "Id"))
                {
                    errs.Add(new ModuleDesignCheckInfo
                    {
                        Location = new ModuleDesignDataLocation { Module = m.Name, Member = m.Name },
                        Message = "データの作成・更新・削除を行うモジュールには、主キーが必要です。",
                    });
                }
                return errs;
            };
            var chat = new ModuleSettingsFunction(host, settings, AiFunctionFactory.ToFieldContext(editor)!);

            var reply = await chat.ProcessMessage("このモジュールのDBテーブル名を入れて");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(module));

            Assert.That(module.DbTable, Is.Not.Empty, "テーブル名が設定されていない(主キー未整備の助言で弾かれた?)");
            Assert.That(editor.UpdateCount, Is.GreaterThan(0), "適用されていない(Update が呼ばれていない)");
        }

        // 請求書(親) + 請求書明細(子) を用意し、請求書を編集する OverallSettingsChat を作る。
        static (FieldCreateFunction Chat, ModuleDesign Invoice) CreateInvoiceChat(Func<IChatClient> settings)
        {
            var invoice = new ModuleDesign { Name = "請求書", DataSourceName = "Main", DbTable = "invoice" };
            invoice.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            invoice.Fields.Add(new TextFieldDesign { Name = "InvoiceNo", DbColumn = "invoice_no" });

            var detail = new ModuleDesign { Name = "請求書明細", DataSourceName = "Main", DbTable = "invoice_detail" };
            detail.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            detail.Fields.Add(new IdFieldDesign { Name = "InvoiceId", DbColumn = "invoice_id" });
            detail.Fields.Add(new TextFieldDesign { Name = "ItemName", DbColumn = "item_name" });

            var designData = new DesignData { Modules = new FakeModuleDesigns(new[] { invoice, detail }) };
            var dataSources = new List<DataSource>
            {
                new DataSource { Name = "Main", DataSourceType = DataSourceType.SQLite }
            };
            var host = new FakeChatHost(designData, dataSources);
            var editor = new FakeOverallSettingsEditor(invoice);
            return (new FieldCreateFunction(host, settings, AiFunctionFactory.ToFieldContext(editor)!), invoice);
        }

        // 「リストを追加して、モジュールは請求書明細を使って」/「明細を追加して、…」のように
        // 参照先モジュール名に「明細」が含まれていても、既定は表形式の ListFieldDesign を選ぶこと
        // (DetailListFieldDesign を選んでしまう不具合の回帰防止)。
        [TestCase("リストを追加して、モジュールは請求書明細を使って")]
        [TestCase("明細を追加して、モジュールは請求書明細を使って")]
        public async Task 明細を含むモジュール名でも既定はListFieldを選ぶ(string instruction)
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (chat, invoice) = CreateInvoiceChat(settings);

            var reply = await chat.ProcessMessage(instruction);

            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(invoice));

            var detailList = invoice.Fields.OfType<DetailListFieldDesign>().FirstOrDefault();
            Assert.That(detailList, Is.Null, "DetailListFieldDesign が選ばれている(モジュール名の「明細」に引きずられた誤り)");

            var list = invoice.Fields.OfType<ListFieldDesign>().FirstOrDefault();
            Assert.That(list, Is.Not.Null, "ListFieldDesign が追加されていない");
            Assert.That(list!.SearchCondition.ModuleName, Is.EqualTo("請求書明細"),
                "ListField が請求書明細モジュールを参照していない");
        }
    }
}

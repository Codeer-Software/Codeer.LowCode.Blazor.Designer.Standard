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
    /// 権限・アクセス条件(UserRead/Write・DataRead/Write Condition)の生成検証。
    /// 条件は MatchConditionBase のポリモーフィック木で、TypeFullName や Value のラップを誤ると
    /// 「設計が読み込めず画面が真っ白」になる致命的領域。よって「適用される + ModuleDesign 全体が
    /// シリアライズ往復で壊れない(=ロード可能) + 対象フィールドを参照している」を検証する。
    /// </summary>
    [TestFixture]
    public class ConditionTest
    {
        static (ModuleSettingsFunction Chat, ModuleDesign Module) NewChat(Func<IChatClient> settings)
        {
            var module = new ModuleDesign { Name = "Task", DataSourceName = "Main", DbTable = "task" };
            module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            module.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "title" });
            module.Fields.Add(new BooleanFieldDesign { Name = "LogicalDelete", DbColumn = "logical_delete" });
            module.Fields.Add(new SelectFieldDesign { Name = "Status", DbColumn = "status", Candidates = new() { "未着手,0", "完了,1" } });
            module.Fields.Add(new LinkFieldDesign { Name = "Creator", DbColumn = "creator" });

            var designData = new DesignData { Modules = new FakeModuleDesigns(new[] { module }) };
            var dataSources = new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } };
            var host = new FakeChatHost(designData, dataSources);
            return (new ModuleSettingsFunction(host, settings, AiFunctionFactory.ToFieldContext(new FakeOverallSettingsEditor(module))!), module);
        }

        // ModuleDesign 全体がシリアライズ往復で例外なく復元できること(=設計として読み込める＝画面が真っ白にならない)。
        static void AssertRoundTrips(ModuleDesign module)
        {
            var json = JsonConverterEx.SerializeObject(module);
            ModuleDesign? restored = null;
            Assert.DoesNotThrow(() => restored = JsonConverterEx.DeserializeObject<ModuleDesign>(json),
                "ModuleDesign がシリアライズ往復で壊れる(条件の TypeFullName/Value ラップ不正の疑い=画面真っ白)");
            Assert.That(restored, Is.Not.Null, "ModuleDesign の復元結果が null");
        }

        [Test]
        public async Task DataReadCondition_値一致のフィルタを設定()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (chat, module) = NewChat(settings);

            var reply = await chat.ProcessMessage(
                "DataReadCondition に「論理削除フラグ(LogicalDelete)が false のデータだけ表示する」行フィルタ条件を設定してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(module.DataReadCondition));

            Assert.That(module.DataReadCondition?.Condition, Is.Not.Null, "DataReadCondition に条件が設定されていない");
            AssertRoundTrips(module);
            Assert.That(JsonConverterEx.SerializeObject(module.DataReadCondition), Does.Contain("LogicalDelete"),
                "条件が LogicalDelete を参照していない");
        }

        [Test]
        public async Task DataReadCondition_AND複合条件を設定()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (chat, module) = NewChat(settings);

            var reply = await chat.ProcessMessage(
                "DataReadCondition に「論理削除(LogicalDelete)が false かつ ステータス(Status)が 完了(値 1)」の AND 複合条件を設定してください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(module.DataReadCondition));

            Assert.That(module.DataReadCondition?.Condition, Is.Not.Null, "DataReadCondition に条件が設定されていない");
            AssertRoundTrips(module);
            var json = JsonConverterEx.SerializeObject(module.DataReadCondition);
            Assert.That(json, Does.Contain("LogicalDelete"), "AND条件に LogicalDelete が含まれない");
            Assert.That(json, Does.Contain("Status"), "AND条件に Status が含まれない");
            Assert.That(json, Does.Contain("MultiMatchCondition"), "複合(MultiMatchCondition)になっていない");
        }

        [Test]
        public async Task DataReadCondition_現在ユーザーの行レベルセキュリティを設定()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (chat, module) = NewChat(settings);

            var reply = await chat.ProcessMessage(
                "DataReadCondition に「作成者(Creator)が現在のログインユーザーと一致するデータだけ表示する」行レベルセキュリティ条件を設定してください。");
            TestContext.WriteLine(reply);
            var json = JsonConverterEx.SerializeObject(module.DataReadCondition);
            TestContext.WriteLine(json);

            Assert.That(module.DataReadCondition?.Condition, Is.Not.Null, "DataReadCondition に条件が設定されていない");
            AssertRoundTrips(module);
            Assert.That(json, Does.Contain("Creator"), "条件が Creator を参照していない");
            // 現在ユーザーは CurrentUser.* で参照する(Creator.Value 同士の自己比較は誤り)。
            Assert.That(json, Does.Contain("CurrentUser"), "現在ユーザー参照(CurrentUser.*)になっていない(自己比較の疑い)");
        }

        [Test]
        public async Task UserReadCondition_ロールでアクセス制御を設定()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (chat, module) = NewChat(settings);

            var reply = await chat.ProcessMessage(
                "UserReadCondition に「ログインユーザーのロール(Role)が admin のときだけこのモジュールにアクセスできる」条件を設定してください。");
            TestContext.WriteLine(reply);
            var json = JsonConverterEx.SerializeObject(module.UserReadCondition);
            TestContext.WriteLine(json);

            Assert.That(module.UserReadCondition?.Condition, Is.Not.Null, "UserReadCondition に条件が設定されていない");
            AssertRoundTrips(module);
            Assert.That(json, Does.Contain("Role"), "条件が Role を参照していない");
            Assert.That(json, Does.Contain("admin"), "条件が admin を比較していない");
            // 固定値比較は Value を型付きでラップする(StringValue)。
            Assert.That(json, Does.Contain("StringValue"), "比較値が StringValue でラップされていない");
        }

        [Test]
        public async Task DataReadCondition_OR_NotIn_の複合条件を設定()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (chat, module) = NewChat(settings);

            var reply = await chat.ProcessMessage(
                "DataReadCondition に「Status が 0 と 1 のいずれでもない(NotIn)、または LogicalDelete が true」という OR 複合条件を設定してください。");
            TestContext.WriteLine(reply);
            var json = JsonConverterEx.SerializeObject(module.DataReadCondition);
            TestContext.WriteLine(json);

            Assert.That(module.DataReadCondition?.Condition, Is.Not.Null, "条件が設定されていない");
            AssertRoundTrips(module);
            Assert.That(json, Does.Contain("Status"), "Status を参照していない");
            Assert.That(json, Does.Contain("LogicalDelete"), "LogicalDelete を参照していない");
            Assert.That(json, Does.Contain("\"IsOrMatch\": true"), "OR(IsOrMatch:true)になっていない");
            Assert.That(json, Does.Contain("NotIn"), "NotIn 比較になっていない");
        }
    }
}

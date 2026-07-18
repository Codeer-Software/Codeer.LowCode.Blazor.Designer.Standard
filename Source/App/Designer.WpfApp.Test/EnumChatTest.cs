using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// デザイン enum (新機能) を AIChat が正しく扱えるかの実AIテスト。
    /// プロジェクトに共有 enum が定義されているとき、固定候補の SelectField を Candidates 直書きではなく
    /// EnumName 参照で作れるか (= ModuleEditFunctionBase の enum コンテキスト注入が効いているか) を検証する。
    /// </summary>
    [TestFixture]
    public class EnumChatTest
    {
        static (DesignData designData, ModuleDesign module, List<DataSource> dataSources) BuildOrderProjectWithEnum()
        {
            var module = new ModuleDesign { Name = "Order", DataSourceName = "Main", DbTable = "orders" };
            module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            module.Fields.Add(new TextFieldDesign { Name = "OrderNo", DbColumn = "order_no", DisplayName = "伝票番号" });

            var orderStatus = new EnumDesign
            {
                Name = "OrderStatus",
                ValueType = EnumValueType.String,
                Members =
                {
                    new EnumMemberDesign { Name = "Received", Value = "1", DisplayText = "受注" },
                    new EnumMemberDesign { Name = "Shipped", DisplayText = "出荷済" },
                    new EnumMemberDesign { Name = "Closed", DisplayText = "完了" },
                },
            };

            var designData = new DesignData
            {
                Modules = new FakeModuleDesigns(new[] { module }),
                Enums = new() { orderStatus },
            };
            var dataSources = new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } };
            return (designData, module, dataSources);
        }

        [Test]
        public async Task 共有enumが定義済みならSelectFieldをEnumName参照で作成する()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (designData, module, dataSources) = BuildOrderProjectWithEnum();
            var host = new FakeChatHost(designData, dataSources);
            var chat = new FieldCreateFunction(host, settings, AiFunctionFactory.ToFieldContext(new FakeOverallSettingsEditor(module))!);

            var reply = await chat.ProcessMessage(
                "受注状態を選ぶ項目 Status を追加してください。状態は「受注 / 出荷済 / 完了」で、" +
                "これはこのプロジェクトの他のモジュールでも共通して使う固定の状態です。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(JsonConverterEx.SerializeObject(module));

            var status = module.Fields.OfType<SelectFieldDesign>().FirstOrDefault(f => f.Name == "Status");
            Assert.That(status, Is.Not.Null, "Status (SelectField) が追加されていない");
            Assert.That(status!.EnumName, Is.EqualTo("OrderStatus"),
                "共有 enum が定義されているのに EnumName で参照せず Candidates 直書きになっている " +
                $"(EnumName='{status.EnumName}', Candidates=[{string.Join(",", status.Candidates)}])");
        }

        [Test]
        public async Task スクリプトでenum参照フィールドにenumメンバーで代入する()
        {
            var settings = TestEnv.RequireChatClientFactory();
            var (designData, module, _) = BuildOrderProjectWithEnum();
            // enum を参照する SelectField を持たせる (この Value にはメンバーで代入させたい)。
            module.Fields.Add(new SelectFieldDesign { Name = "Status", DbColumn = "status", EnumName = "OrderStatus" });

            var host = new FakeChatHost(designData, designData.Modules.GetModuleNames()
                .Select(_ => new DataSource { Name = "Main", DataSourceType = DataSourceType.SQLite }).ToList());
            var editor = new FakeScriptEditor("", "Order");
            var chat = new ScriptFunction(host, settings, AiFunctionFactory.ToScriptContext(editor)!);

            var reply = await chat.ProcessMessage(
                "ボタンの OnClick で、この受注の状態 Status を『受注 (Received)』の状態にセットする処理 Receive_OnClick を書いてください。");
            TestContext.WriteLine(reply);
            TestContext.WriteLine(editor.Script);

            // enum メンバー参照 (OrderStatus.Received) を使い、保存値のマジック文字列("1"や"受注")を直書きしていないこと。
            Assert.That(editor.Script, Does.Contain("OrderStatus.Received"),
                "enum 参照フィールドへの代入なのに enum メンバー (OrderStatus.Received) を使っていない。生成スクリプト:\n" + editor.Script);
        }
    }
}

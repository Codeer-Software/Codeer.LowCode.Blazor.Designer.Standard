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
    /// フィールド編集/作成でイベントプロパティを設定したとき、参照するハンドラ関数を
    /// ホスト経由で自動作成する仕組み(EnsureEventHandlers)の決定的テスト(実 AI を使わない)。
    /// </summary>
    [TestFixture]
    public class FieldEventHandlerUnitTest
    {
        static FakeChatHost Host(DesignData designData)
            => new(designData, new List<DataSource> { new() { Name = "Main", DataSourceType = DataSourceType.SQLite } });

        static DesignData Design(params ModuleDesign[] modules)
            => new() { Modules = new FakeModuleDesigns(modules) };

        // AI 応答(IO 形式 JSON)を実オブジェクトのシリアライズで作る(手書き JSON の型ずれを避ける)。
        static string IoResponse(string moduleName, List<FieldDesignBase> fields, string explanation = "変更しました")
            => JsonConverterEx.SerializeObject(new ModuleEditFunctionBase.IO
            {
                ModuleName = moduleName,
                ModuleDesign = new ModuleEditFunctionBase.ModuleDesignEditing { Fields = fields },
                Explanation = explanation,
            });

        [Test]
        public async Task フィールド編集でOnClickを設定するとハンドラが自動作成される()
        {
            var module = new ModuleDesign
            {
                Name = "Order",
                Fields = { new ButtonFieldDesign { Name = "SaveButton" } },
            };
            var editor = new FakeOverallSettingsEditor(module);
            var host = Host(Design(module));

            var edited = new List<FieldDesignBase> { new ButtonFieldDesign { Name = "SaveButton", OnClick = "SaveButton_OnClick" } };
            var client = new ScriptedChatClient(IoResponse("Order", edited, "クリックイベントを設定しました"));
            var fn = new FieldEditFunction(host, () => client, AiFunctionFactory.ToFieldContext(editor)!);

            var result = await fn.ExecuteAsync("保存ボタンにクリックイベントを追加して");

            Assert.That(result.Outcome, Is.EqualTo(FunctionOutcome.Done));
            Assert.That(editor.EnsuredEventHandlers, Is.EqualTo(new[] { ("SaveButton", "OnClick") }),
                "変更したイベントプロパティに対してハンドラ作成が呼ばれていない");
            Assert.That(module.Fields.OfType<ButtonFieldDesign>().Single().OnClick, Is.EqualTo("SaveButton_OnClick"));
            Assert.That(result.Message, Does.Contain("SaveButton_OnClick"), "作成したハンドラ名がユーザーへの説明に入っていない");
            Assert.That(result.Message, Does.Contain("イベントハンドラを作成しました"));
        }

        [Test]
        public async Task 変更していない既存のイベント値にはハンドラ作成を行わない()
        {
            var module = new ModuleDesign
            {
                Name = "Order",
                Fields = { new ButtonFieldDesign { Name = "SaveButton", OnClick = "AlreadySet" } },
            };
            var editor = new FakeOverallSettingsEditor(module);
            var host = Host(Design(module));

            // OnClick はそのまま(変更なし)の応答。
            var edited = new List<FieldDesignBase> { new ButtonFieldDesign { Name = "SaveButton", OnClick = "AlreadySet" } };
            var client = new ScriptedChatClient(IoResponse("Order", edited));
            var fn = new FieldEditFunction(host, () => client, AiFunctionFactory.ToFieldContext(editor)!);

            var result = await fn.ExecuteAsync("特に変更なし");

            Assert.That(result.Outcome, Is.EqualTo(FunctionOutcome.Done));
            Assert.That(editor.EnsuredEventHandlers, Is.Empty, "AIが触っていないイベント値にハンドラ作成が走っている");
        }

        [Test]
        public async Task フィールド作成でイベント付きボタンを追加するとハンドラが自動作成される()
        {
            var module = new ModuleDesign { Name = "Order" };
            var editor = new FakeOverallSettingsEditor(module);
            var host = Host(Design(module));

            var created = new List<FieldDesignBase> { new ButtonFieldDesign { Name = "PrintButton", OnClick = "PrintButton_OnClick" } };
            var client = new ScriptedChatClient(IoResponse("Order", created, "印刷ボタンを追加しました"));
            var fn = new FieldCreateFunction(host, () => client, AiFunctionFactory.ToFieldContext(editor)!);

            var result = await fn.ExecuteAsync("印刷ボタンを追加してクリックイベントも付けて");

            Assert.That(result.Outcome, Is.EqualTo(FunctionOutcome.Done));
            Assert.That(editor.EnsuredEventHandlers, Is.EqualTo(new[] { ("PrintButton", "OnClick") }));
            Assert.That(result.Message, Does.Contain("PrintButton_OnClick"));
        }

        [Test]
        public async Task ハンドラが既に存在する場合は作成メッセージを出さない()
        {
            var module = new ModuleDesign
            {
                Name = "Order",
                Fields = { new ButtonFieldDesign { Name = "SaveButton" } },
            };
            var editor = new FakeOverallSettingsEditor(module);
            var host = Host(Design(module));
            editor.EnsureEventHandlerOverride = (_, _) => false; // 既存関数あり=作成しなかった

            var edited = new List<FieldDesignBase> { new ButtonFieldDesign { Name = "SaveButton", OnClick = "ExistingFunc" } };
            var client = new ScriptedChatClient(IoResponse("Order", edited));
            var fn = new FieldEditFunction(host, () => client, AiFunctionFactory.ToFieldContext(editor)!);

            var result = await fn.ExecuteAsync("保存ボタンのクリックで ExistingFunc を呼んで");

            Assert.That(result.Outcome, Is.EqualTo(FunctionOutcome.Done));
            Assert.That(editor.EnsuredEventHandlers, Has.Count.EqualTo(1), "存在確認自体は呼ばれるはず");
            Assert.That(result.Message, Does.Not.Contain("イベントハンドラを作成しました"), "作成していないのに作成したと報告している");
        }
    }
}

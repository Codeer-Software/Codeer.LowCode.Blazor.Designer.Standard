using Codeer.LowCode.Blazor.Designer.Standard.ModuleToClass;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using System.IO;
using System.Reflection;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// ClassGenerator のテスト。DesignToDataFieldClasses (poco コマンド) は生成した C# を Roslyn でコンパイルし、
    /// プロパティ名と型がデザインのフィールドと一致することを確かめる。
    /// </summary>
    [TestFixture]
    public class ClassGeneratorTest
    {
        const string Namespace = "Poco.Test.Modules";

        static DesignData CreateDesign()
        {
            var design = new DesignData();
            var modules = (IEditableModuleDesign)design.Modules;

            modules.Add(new ModuleDesign
            {
                Name = "Order",
                Fields =
                {
                    new IdFieldDesign { Name = "Id" },
                    new TextFieldDesign { Name = "Name", DisplayName = "顧客名 <A&B>" },
                    new NumberFieldDesign { Name = "Amount" },
                    new DateFieldDesign { Name = "OrderedOn" },
                    new BooleanFieldDesign { Name = "IsClosed" },
                    new SelectFieldDesign { Name = "Status" },
                    new ButtonFieldDesign { Name = "CloseButton" },
                    new LabelFieldDesign { Name = "Caption" },
                    new TextFieldDesign { Name = "class" },
                    new TextFieldDesign { Name = "Order" },
                    new TextFieldDesign { Name = "1st" },
                    new TextFieldDesign { Name = "Customer.Name" },
                },
            });

            // データを持つフィールドが無いモジュール
            modules.Add(new ModuleDesign
            {
                Name = "Menu",
                Fields = { new ButtonFieldDesign { Name = "Go" }, new LabelFieldDesign { Name = "Title" } },
            });

            // 予約語のモジュール名
            modules.Add(new ModuleDesign
            {
                Name = "event",
                Fields = { new IdFieldDesign { Name = "Id" }, new TextFieldDesign { Name = "Name" } },
            });

            // FieldData 型と同じ単純名のモジュール (Order.Status の型は完全修飾で書かれる)
            modules.Add(new ModuleDesign
            {
                Name = nameof(SelectFieldData),
                Fields = { new IdFieldDesign { Name = "Id" } },
            });
            return design;
        }

        static Assembly Compile(string code)
        {
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(e => !e.IsDynamic && !string.IsNullOrEmpty(e.Location))
                .Select(e => MetadataReference.CreateFromFile(e.Location))
                .ToList();
            var compilation = CSharpCompilation.Create(
                "ClassGeneratorTest_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(code) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

            using var stream = new MemoryStream();
            var emitted = compilation.Emit(stream);
            var errors = emitted.Diagnostics.Where(e => e.Severity == DiagnosticSeverity.Error).Select(e => e.ToString()).ToList();
            Assert.That(errors, Is.Empty, code);
            return Assembly.Load(stream.ToArray());
        }

        static string[] PropertyNames(Type type)
            => type.GetProperties().OrderBy(e => e.MetadataToken).Select(e => e.Name).ToArray();

        [Test]
        public void 生成したコードがコンパイルでき_プロパティがフィールドと一致する()
        {
            var result = ClassGenerator.DesignToDataFieldClasses(CreateDesign(), Namespace);
            var assembly = Compile(result.Code);

            // モジュールは名前順 (序数比較。データを持たない Menu は出ない)
            Assert.That(result.Modules, Is.EqualTo(new[] { "Order", nameof(SelectFieldData), "event" }));

            var order = assembly.GetType($"{Namespace}.Order")!;
            Assert.That(order, Is.Not.Null);
            Assert.That(PropertyNames(order), Is.EqualTo(new[] { "Id", "Name", "Amount", "OrderedOn", "IsClosed", "Status", "class" }));
            Assert.That(order.GetProperty("Id")!.PropertyType, Is.EqualTo(typeof(IdFieldData)));
            Assert.That(order.GetProperty("Name")!.PropertyType, Is.EqualTo(typeof(TextFieldData)));
            Assert.That(order.GetProperty("Amount")!.PropertyType, Is.EqualTo(typeof(NumberFieldData)));
            Assert.That(order.GetProperty("OrderedOn")!.PropertyType, Is.EqualTo(typeof(DateFieldData)));
            Assert.That(order.GetProperty("IsClosed")!.PropertyType, Is.EqualTo(typeof(BooleanFieldData)));
            Assert.That(order.GetProperty("Status")!.PropertyType, Is.EqualTo(typeof(SelectFieldData)));
            Assert.That(order.GetProperty("class")!.PropertyType, Is.EqualTo(typeof(TextFieldData)));

            var reserved = assembly.GetType($"{Namespace}.event")!;
            Assert.That(reserved, Is.Not.Null);
            Assert.That(PropertyNames(reserved), Is.EqualTo(new[] { "Id", "Name" }));

            var sameAsDataType = assembly.GetType($"{Namespace}.{nameof(SelectFieldData)}")!;
            Assert.That(PropertyNames(sameAsDataType), Is.EqualTo(new[] { "Id" }));

            Assert.That(assembly.GetType($"{Namespace}.Menu"), Is.Null);
        }

        [Test]
        public void 出さなかったものは警告に入る()
        {
            var result = ClassGenerator.DesignToDataFieldClasses(CreateDesign(), Namespace);

            Assert.That(result.Warnings.Count, Is.EqualTo(3), string.Join(Environment.NewLine, result.Warnings));
            Assert.That(result.Warnings.Any(e => e.StartsWith("Order.Order:")), Is.True);
            Assert.That(result.Warnings.Any(e => e.StartsWith("Order.1st:")), Is.True);
            Assert.That(result.Warnings.Any(e => e.StartsWith("Menu:")), Is.True);
        }

        [TestCase("LowCodeApp.Client.Shared.Modules", true)]
        [TestCase("業務.モジュール", true)]
        [TestCase("A..B", false)]
        [TestCase("A.class", false)]
        [TestCase("A-B", false)]
        public void 名前空間の妥当性(string @namespace, bool expected)
            => Assert.That(ClassGenerator.IsValidNamespace(@namespace), Is.EqualTo(expected));

        [Test]
        public void 生成物の形()
        {
            var code = ClassGenerator.DesignToDataFieldClasses(CreateDesign(), Namespace).Code;

            Assert.That(code, Does.StartWith("// <auto-generated>\r\n"));
            Assert.That(code, Does.Contain("#nullable enable\r\n"));
            Assert.That(code.Replace("\r\n", string.Empty), Does.Not.Contain("\n"));
            Assert.That(code, Does.Contain("using Codeer.LowCode.Blazor.Repository.Data;"));
            Assert.That(code, Does.Contain("public partial class @event"));
            Assert.That(code, Does.Contain("public TextFieldData? @class { get; set; }"));
            Assert.That(code, Does.Contain("public global::Codeer.LowCode.Blazor.Repository.Data.SelectFieldData? Status { get; set; }"));
            Assert.That(code, Does.Contain("/// <summary>顧客名 &lt;A&amp;B&gt; (TextField)</summary>"));
            Assert.That(code, Does.Contain("/// <summary>NumberField</summary>"));
        }

        [Test]
        public void モジュール右クリックのFieldDataクラスの形は変わらない()
        {
            var module = new ModuleDesign
            {
                Name = "Order",
                Fields =
                {
                    new IdFieldDesign { Name = "Id" },
                    new TextFieldDesign { Name = "Name" },
                    new ButtonFieldDesign { Name = "CloseButton" },
                    new TextFieldDesign { Name = "Customer.Name" },
                },
            };

            var expected = string.Join(Environment.NewLine,
                "public class Order",
                "{",
                "    public IdFieldData? Id { get; set; }",
                "    public TextFieldData? Name { get; set; }",
                "}",
                string.Empty);
            Assert.That(ClassGenerator.ModuleDesignToDataFieldClass(module), Is.EqualTo(expected));
        }
    }
}

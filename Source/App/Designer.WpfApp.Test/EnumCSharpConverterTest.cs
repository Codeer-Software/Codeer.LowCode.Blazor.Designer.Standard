using Codeer.LowCode.Blazor.Designer.Standard.ModuleToClass;
using Codeer.LowCode.Blazor.Repository.Design;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// EnumDesign → C# enum宣言の生成(EnumCSharpConverter.Generate)のテスト。
    /// </summary>
    [TestFixture]
    public class EnumCSharpConverterTest
    {
        [Test]
        public void String型は名前だけの宣言になる()
        {
            var src = new EnumDesign
            {
                Name = "OrderStatus",
                Members =
                [
                    new() { Name = "Received", Value = "1", DisplayText = "受注" },
                    new() { Name = "Shipped" },
                ]
            };
            var text = EnumCSharpConverter.Generate(src);
            Assert.That(text, Does.Contain("enum OrderStatus"));
            Assert.That(text, Does.Contain("[DisplayText(\"受注\")]"));
            Assert.That(text, Does.Contain("    Received,"));
            Assert.That(text, Does.Contain("    Shipped,"));
            //保存値([Value]属性や初期化子)は出力しない(C#側はメンバー名ベースで扱う)
            Assert.That(text, Does.Not.Contain("Value("));
            Assert.That(text, Does.Not.Contain("= 1"));
        }

        [Test]
        public void Number型は保存値が数値初期化子として出る()
        {
            var src = new EnumDesign
            {
                Name = "Priority",
                ValueType = EnumValueType.Number,
                Members =
                [
                    new() { Name = "Low", Value = "1", DisplayText = "低" },
                    new() { Name = "High", Value = "5" },
                ]
            };
            var text = EnumCSharpConverter.Generate(src);
            Assert.That(text, Does.Contain("enum Priority"));
            Assert.That(text, Does.Contain("[DisplayText(\"低\")]"));
            Assert.That(text, Does.Contain("    Low = 1,"));
            Assert.That(text, Does.Contain("    High = 5,"));
        }

        [Test]
        public void 表示名の引用符とバックスラッシュはエスケープされる()
        {
            var src = new EnumDesign
            {
                Name = "E",
                Members = [new() { Name = "A", DisplayText = "引用\"と\\マーク" }]
            };
            var text = EnumCSharpConverter.Generate(src);
            Assert.That(text, Does.Contain("[DisplayText(\"引用\\\"と\\\\マーク\")]"));
        }
    }
}

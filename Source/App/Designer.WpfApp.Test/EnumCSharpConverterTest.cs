using Codeer.LowCode.Blazor.Designer.Standard.ModuleToClass;
using Codeer.LowCode.Blazor.Repository.Design;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// EnumDesign → C# enum宣言の生成(EnumCSharpConverter.Generate)のテスト。
    /// 出力はメンバー名ベース(保存値・表示名は出力しない)。Number型だけ保存値を数値初期化子として出す。
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
            Assert.That(text, Does.Contain("    Received,"));
            Assert.That(text, Does.Contain("    Shipped,"));
            //保存値・表示名は出力しない(C#側はメンバー名ベースで扱う)
            Assert.That(text, Does.Not.Contain("Value("));
            Assert.That(text, Does.Not.Contain("DisplayText"));
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
            Assert.That(text, Does.Contain("    Low = 1,"));
            Assert.That(text, Does.Contain("    High = 5,"));
            Assert.That(text, Does.Not.Contain("DisplayText"));
        }
    }
}

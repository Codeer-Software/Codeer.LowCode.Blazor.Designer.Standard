using System.Text;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Designer.Standard.ModuleToClass
{
    /// <summary>
    /// EnumDesign から C# の enum 宣言を生成する (Create C# Enum メニュー)。
    /// ドキュメント・コード連携用途。保存値は出力しない(C#側はメンバー名ベースで扱うため)。
    ///
    /// 出力例:
    ///   enum OrderStatus
    ///   {
    ///       Received = 1,      // Number型のときは保存値を数値初期化子として出す
    ///       Shipped,
    ///   }
    /// </summary>
    public static class EnumCSharpConverter
    {
        public static string Generate(EnumDesign design)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"enum {design.Name}");
            sb.AppendLine("{");
            foreach (var member in design.Members)
            {
                var initializer = design.ValueType == EnumValueType.Number ? $" = {member.GetValue()}" : string.Empty;
                sb.AppendLine($"    {member.Name}{initializer},");
            }
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}

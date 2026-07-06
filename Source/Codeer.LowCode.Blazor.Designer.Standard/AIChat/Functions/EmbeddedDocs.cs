using System.IO;
using System.Reflection;
using System.Text;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // Lib/AI 配下の仕様 .md(埋め込みリソース)を読み込むための共通ヘルパ。
    // 各機能で重複していたリソース読み込み処理を集約する。リソースの論理名は
    // csproj の EmbeddedResource で "Codeer.LowCode.Blazor.Designer.Standard.AIChat.<file>.md" になる。
    static class EmbeddedDocs
    {
        static readonly Assembly Asm = typeof(EmbeddedDocs).Assembly;

        // 複数のリソースを連結して返す(読み込めないものはスキップ)。
        public static string Load(params string[] resourceNames)
        {
            var sb = new StringBuilder();
            foreach (var name in resourceNames)
            {
                try
                {
                    using var stream = Asm.GetManifestResourceStream(name);
                    if (stream == null) continue;
                    using var reader = new StreamReader(stream);
                    sb.AppendLine(reader.ReadToEnd());
                    sb.AppendLine();
                }
                catch
                {
                    // 読み込めないファイルはスキップ
                }
            }
            return sb.ToString();
        }
    }
}

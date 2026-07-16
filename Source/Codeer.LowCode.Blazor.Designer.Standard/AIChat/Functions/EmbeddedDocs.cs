using Codeer.LowCode.Blazor.Designer.Extensibility;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // AIChat のプロンプトに注入する仕様・ガイドラインの読み込みヘルパ。自前コピーは持たない (単一ソース):
    //  - Spec(...)      : フレームワーク仕様 → Designer 本体の SpecDocCatalog (実装と同一アセンブリ・同一バージョン)
    //  - Guideline(...) : 作り方ガイドライン → このライブラリの ClaudeWorkspace.zip 内 Docs (Claude Code と同一ソース)
    // 読み込めないものはスキップして残りを返す (プロンプトは部分欠落しても動くため)。
    static class EmbeddedDocs
    {
        const string ZipResourceName = "Codeer.LowCode.Blazor.Designer.Standard.ClaudeWorkspace.zip";
        const string DocsPrefix = "ClaudeCodeForDesigner/Docs/";

        // 仕様ドキュメント (SpecDocCatalog の id、例: "Layouts", "_FieldCommon", "JsonAbstractTypeFullName")
        public static string Spec(params string[] ids)
        {
            var sb = new StringBuilder();
            foreach (var id in ids)
            {
                try
                {
                    var doc = SpecDocCatalog.Load(id);
                    if (string.IsNullOrEmpty(doc)) continue;
                    sb.AppendLine(doc);
                    sb.AppendLine();
                }
                catch
                {
                    // 読み込めないものはスキップ
                }
            }
            return sb.ToString();
        }

        // 作り方ガイドライン (ClaudeWorkspace の Docs/ 相対名、例: "LayoutGuidelines.md")
        public static string Guideline(params string[] fileNames)
        {
            var sb = new StringBuilder();
            foreach (var fileName in fileNames)
            {
                try
                {
                    using var stream = typeof(EmbeddedDocs).Assembly.GetManifestResourceStream(ZipResourceName);
                    if (stream == null) continue;
                    using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
                    // zip 生成元によりエントリの区切りが '/' と '\' のどちらもありうるため正規化して探す
                    var wanted = (DocsPrefix + fileName).Replace('\\', '/');
                    var entry = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/') == wanted);
                    if (entry == null) continue;
                    using var reader = new StreamReader(entry.Open());
                    sb.AppendLine(reader.ReadToEnd());
                    sb.AppendLine();
                }
                catch
                {
                    // 読み込めないものはスキップ
                }
            }
            return sb.ToString();
        }
    }
}

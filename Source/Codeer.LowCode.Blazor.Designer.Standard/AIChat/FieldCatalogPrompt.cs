using System.Collections.Generic;
using System.Linq;
using System.Text;
using Codeer.LowCode.Blazor.Designer.Extensibility;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat
{
    // FieldCatalog(中立サービス)を AI プロンプト用の Markdown に整形する消費者側ヘルパ。
    //
    // AIChat のライブ生成には「束ね系レベルのコンパクトなフィールド型カタログ」を FieldCatalog から動的生成して渡す:
    //  - 用途1行 = 各型の FieldDocs(## Design) 冒頭の説明文(キュレーション済み)
    //  - 主なプロパティ = reflection の「その型固有」プロパティ(共通基底は _FieldCommon.md 参照)
    // これで組み込み・外部ライブラリ・独自を統一的に1ソース(FieldCatalog)から出せる。フル解説(全プロパティ表・JSON例・
    // Script/CSS)は CLI のフルカタログ(ClaudeCode 向け)が担い、ライブプロンプトには載せない(トークン肥大回避)。
    static class FieldCatalogPrompt
    {
        // 全フィールド型(組み込み + 外部ライブラリ + 独自)のコンパクトカタログ。ModuleEdit のライブプロンプトに注入する。
        public static string BuildFieldTypeCatalog()
        {
            var types = FieldCatalog.GetFieldTypes();
            if (types.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("## フィールド型カタログ（このプロジェクトで使える全フィールド型。各フィールド定義に TypeFullName を必ず設定）");
            sb.AppendLine();
            sb.AppendLine("共通プロパティ（Name / DisplayName / IsRequired / DbColumn / IgnoreModification / OnValidateInput 等）は各型で共通なので下記には出さない（別途「フィールド共通基底」参照）。下記は各型の用途と“その型固有”のプロパティ。");
            sb.AppendLine();

            foreach (var t in types)
            {
                var ext = t.IsBuiltIn ? "" : "（外部ライブラリ／独自）";
                sb.AppendLine($"### {t.DisplayName}{ext} — `{t.TypeFullName}`");

                var purpose = ExtractPurpose(t.GetDoc(FieldDocSection.Design));
                if (!string.IsNullOrEmpty(purpose)) sb.AppendLine(purpose);

                var ownProps = t.Properties.Where(p => !p.IsInherited).ToList();
                if (ownProps.Count > 0)
                    sb.AppendLine("主なプロパティ: " + string.Join(", ", ownProps.Select(FormatProp)));

                sb.AppendLine();
            }

            return sb.ToString().Trim();
        }

        static string FormatProp(FieldPropertyInfo p)
        {
            var s = $"`{p.Name}`({p.TypeName})";
            if (p.Candidates.Count > 0) s += $"[{string.Join("/", p.Candidates)}]";
            return s;
        }

        // Design ドキュメント冒頭の「用途を述べる最初の平文」を1つ取り出す(見出し/コード/表/リスト/TypeFullName 行は飛ばす)。
        static string ExtractPurpose(string? designDoc)
        {
            if (string.IsNullOrEmpty(designDoc)) return string.Empty;
            foreach (var raw in designDoc.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#") || line.StartsWith("|") || line.StartsWith(">")
                    || line.StartsWith("```") || line.StartsWith("- ") || line.StartsWith("* ")
                    || line.StartsWith("**TypeFullName")) continue;
                return line;
            }
            return string.Empty;
        }

        // Script / CSS セクション: 独自(非組み込み)フィールドの当該セクションのドキュメントを連結(型名見出し付き)。
        // 組み込みのスクリプト/CSS 詳細は言語仕様 Docs / AppCss.md が担う。
        public static string BuildSectionReference(FieldDocSection section)
        {
            var docs = FieldCatalog.GetFieldTypes()
                .Where(t => !t.IsBuiltIn)
                .Select(t => (t, doc: t.GetDoc(section)))
                .Where(x => !string.IsNullOrEmpty(x.doc))
                .ToList();
            if (docs.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            foreach (var (t, doc) in docs)
            {
                sb.AppendLine($"### {t.DisplayName} ({t.TypeFullName})");
                sb.AppendLine(doc);
                sb.AppendLine();
            }
            return sb.ToString().Trim();
        }
    }
}

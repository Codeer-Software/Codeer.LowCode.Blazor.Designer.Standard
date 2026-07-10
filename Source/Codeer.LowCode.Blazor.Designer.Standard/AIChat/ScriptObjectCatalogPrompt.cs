using System.Collections.Generic;
using System.Linq;
using System.Text;
using Codeer.LowCode.Blazor.Designer.Extensibility;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat
{
    // ScriptObjectCatalog(中立サービス)を AI プロンプト用の Markdown に整形する消費者側ヘルパ。
    // FieldCatalogPrompt のスクリプトオブジェクト版。
    //
    // ScriptFunction のライブ生成には「この環境に実際に登録されているサービス/型」を動的生成して渡す:
    //  - サービスはメンバーシグネチャ付き(数が少なく、スクリプト生成の主役のため)
    //  - new 可能な型は名前一覧のみ(FieldData 等で数が多い。詳細は登録ドキュメントか _ScriptApi.md が担う)
    //  - 登録ドキュメント(Extras / 独自ライブラリが ScriptObjectCatalog.Add したもの)は使い方の正として全文添付
    // これで組み込み・外部ライブラリ・独自を統一的に1ソース(ScriptObjectCatalog)から出せる。
    static class ScriptObjectCatalogPrompt
    {
        public static string Build()
        {
            var objects = ScriptObjectCatalog.GetScriptObjects();
            if (objects.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("## この環境で使えるスクリプトオブジェクト（登録済みのサービス・型・列挙型。ここに無いサービスを生成しないこと）");
            sb.AppendLine();

            var services = objects.Where(x => x.Kind == ScriptObjectKind.Service).ToList();
            if (services.Count > 0)
            {
                sb.AppendLine("### サービス（サービス名で直接アクセス）");
                sb.AppendLine();
                foreach (var s in services)
                {
                    sb.AppendLine($"- **{s.Name}**: {string.Join(" / ", s.Members.Select(m => $"`{m}`"))}");
                }
                sb.AppendLine();
            }

            var statics = objects.Where(x => x.Kind == ScriptObjectKind.StaticType && x.Members.Count > 0).ToList();
            if (statics.Count > 0)
            {
                sb.AppendLine("### 静的型（型名で静的メンバーにアクセス）");
                sb.AppendLine();
                foreach (var s in statics)
                {
                    sb.AppendLine($"- **{s.Name}**: {string.Join(" / ", s.Members.Select(m => $"`{m}`"))}");
                }
                sb.AppendLine();
            }

            var creatables = objects.Where(x => x.Kind == ScriptObjectKind.CreatableType).ToList();
            if (creatables.Count > 0)
            {
                sb.AppendLine("### new で生成できる型");
                sb.AppendLine();
                sb.AppendLine(string.Join(", ", creatables.Select(x => $"`{x.Name}`")));
                sb.AppendLine();
            }

            var enums = objects.Where(x => x.Kind == ScriptObjectKind.EnumType).ToList();
            if (enums.Count > 0)
            {
                sb.AppendLine("### 列挙型");
                sb.AppendLine();
                foreach (var e in enums)
                {
                    sb.AppendLine($"- `{e.Name}`: {string.Join(", ", e.EnumValues)}");
                }
                sb.AppendLine();
            }

            // 登録ドキュメント(使い方・例)。Extras / 独自ライブラリが ScriptObjectCatalog.Add したもの。
            var docs = objects.Where(x => !string.IsNullOrEmpty(x.Doc)).ToList();
            if (docs.Count > 0)
            {
                sb.AppendLine("### 使い方（登録ドキュメント）");
                sb.AppendLine();
                foreach (var d in docs)
                {
                    sb.AppendLine($"#### {d.Name}");
                    sb.AppendLine();
                    if (d.Kind == ScriptObjectKind.CreatableType && d.Constructors.Count > 0)
                    {
                        foreach (var c in d.Constructors) sb.AppendLine($"- `{c}`");
                        sb.AppendLine();
                    }
                    sb.AppendLine(d.Doc);
                    sb.AppendLine();
                }
            }

            return sb.ToString().Trim();
        }
    }
}

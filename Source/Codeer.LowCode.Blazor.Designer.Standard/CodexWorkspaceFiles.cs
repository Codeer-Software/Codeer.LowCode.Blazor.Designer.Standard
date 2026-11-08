using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Designer.Standard
{
    /// <summary>
    /// Codex 用の許可とフック (.codex/hooks.json と .codex/rules/codeer-lowcode.rules) を、Claude Code 用の
    /// settings.local.json のサンプル (exe パス焼き込み済み) から作る。許可とフックの正本は Claude 用のサンプル 1 つ。
    ///   - hooks.json: サンプルの hooks 節をそのまま使う (Codex のフックは同じ形。PreToolUse の Write|Edit は apply_patch にも当たる)
    ///   - rules: サンプルの permissions.allow のうち Bash(...:*) を prefix_rule にする
    /// GitHub Copilot は .claude/settings.local.json のフックをそのまま読むので、Copilot 用のファイルは作らない。
    /// </summary>
    static class CodexWorkspaceFiles
    {
        public const string HooksRelativePath = ".codex/hooks.json";
        public const string RulesRelativePath = ".codex/rules/codeer-lowcode.rules";

        /// <summary>
        /// rootDir に Codex 用の 2 ファイルを書く。overwrite が false なら既存のファイルは触らない。
        /// 書いたファイル (相対パス) を written に、既存のため触らなかったものを preserved に入れる。
        /// </summary>
        public static void Write(string rootDir, JsonObject claudeSettingsSample, bool overwrite, List<string> written, List<string> preserved)
        {
            if (claudeSettingsSample["hooks"] is JsonObject hooks)
            {
                var json = new JsonObject { ["hooks"] = hooks.DeepClone() }.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                });
                WriteFile(rootDir, HooksRelativePath, json, overwrite, written, preserved);
            }

            if (claudeSettingsSample["permissions"]?["allow"] is JsonArray allow)
            {
                var rules = CreateRules(allow.Select(x => x?.GetValue<string>()).OfType<string>());
                if (rules != null) WriteFile(rootDir, RulesRelativePath, rules, overwrite, written, preserved);
            }
        }

        // Bash("<exe>" verb:*) は exe ごとに verb をまとめ、Bash(dotnet build:*) は空白で区切った語の並びにする
        static string? CreateRules(IEnumerable<string> allow)
        {
            var exeVerbs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var commands = new List<string[]>();
            foreach (var item in allow)
            {
                var m = Regex.Match(item, @"^Bash\((.+):\*\)$");
                if (!m.Success) continue;
                var body = m.Groups[1].Value;
                var quoted = Regex.Match(body, "^\"([^\"]+)\" (\\S+)$");
                if (quoted.Success)
                {
                    if (!exeVerbs.TryGetValue(quoted.Groups[1].Value, out var verbs))
                        exeVerbs[quoted.Groups[1].Value] = verbs = new List<string>();
                    verbs.Add(quoted.Groups[2].Value);
                }
                else
                {
                    commands.Add(body.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                }
            }
            if (exeVerbs.Count == 0 && commands.Count == 0) return null;

            var sb = new StringBuilder();
            sb.Append("# デザイナ (claude-workspace / developer-workspace) が .claude/settings.local.json と同じ許可で生成する。手で編集しない。\n");
            sb.Append("# Codex はこのプロジェクトを信頼済みにしたときだけ読む。\n");
            foreach (var (exe, verbs) in exeVerbs)
                AppendRule(sb, "[" + Quote(exe) + ", [" + string.Join(", ", verbs.Select(Quote)) + "]]");
            foreach (var words in commands)
                AppendRule(sb, "[" + string.Join(", ", words.Select(Quote)) + "]");
            return sb.ToString();
        }

        static void AppendRule(StringBuilder sb, string pattern)
            => sb.Append("\nprefix_rule(\n    pattern = ").Append(pattern).Append(",\n    decision = \"allow\",\n)\n");

        static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        static void WriteFile(string rootDir, string relativePath, string content, bool overwrite, List<string> written, List<string> preserved)
        {
            var path = Path.Combine(rootDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
            {
                // 中身が同じなら書かない (Codex のフックの信頼は中身のハッシュに結び付くので、書き直しても同じなら確認し直しにならない)
                if (!overwrite || File.ReadAllText(path, Encoding.UTF8) == content)
                {
                    preserved.Add(relativePath);
                    return;
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            written.Add(relativePath);
        }
    }
}

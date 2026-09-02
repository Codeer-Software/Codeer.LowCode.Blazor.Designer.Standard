using Codeer.LowCode.Blazor.Designer.Extensibility;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Designer.Standard
{
    /// <summary>
    /// ホストソリューション (C#) を触る Claude Code 向けの生成リファレンスを、ホストのルートに書き出す。
    ///
    /// 置き場は &lt;root&gt;/ClaudeCodeForDeveloper/。このフォルダはホスト側 (Codeer.LowCode.Blazor.Starter 等) の
    /// 静的な文書 (セットアップ手順書など・コミット対象) と、この verb が生成するもの (パッケージの版に追従する・
    /// 再生成可能) が同居する。**この verb が所有するのは `_` で始まるエントリだけ** (現在は `_specs/`)。
    /// それ以外のファイルには触らない (フォルダを丸ごと作り直す ClaudeCodeForDesigner/ とは契約が違う)。
    ///
    /// 出力はライブラリ (Designer / 拡張ライブラリ) の埋め込み仕様書のうちホスト開発に関わるもの。
    /// デザインプロジェクトが無くても出せる (プロジェクトを開いて生成するカタログ類は
    /// デザインプロジェクト側の ClaudeCodeForDesigner/ が持つ)。
    ///
    /// headless CLI:
    ///   &lt;designer.exe&gt; developer-workspace "&lt;hostRootDir&gt;" [--out "&lt;resultJsonPath&gt;"]
    ///   終了コード: 0 = 成功 / 2 = 失敗。
    ///   verb 登録 (<see cref="RegisterCli"/>) はアプリの OnStartup で base.OnStartup(e) より前に行うこと。
    /// </summary>
    public static class DeveloperWorkspaceDeploy
    {
        public const string Verb = "developer-workspace";
        public const string FolderName = "ClaudeCodeForDeveloper";
        const string SpecsDirName = "_specs";

        // ホスト開発 (C#) に関わる仕様書の id (SpecDocCatalog の id = _specs/<id>.md)。
        // HostCustomization が参照している _FieldCommon / ScriptExtensions も一緒に出す。
        static readonly string[] SpecIds = { "HostCustomization", "_FieldCommon", "ScriptExtensions" };

        /// <summary>headless CLI に developer-workspace verb を登録する (base.OnStartup 前に呼ぶ)。</summary>
        public static void RegisterCli() => HeadlessCliVerbs.Register(Verb, RunCli);

        public class DeployResult
        {
            /// <summary>書き出したファイル (ホストルート相対)。</summary>
            public List<string> Written { get; } = new();
            /// <summary>カタログに無くスキップした仕様書 id。</summary>
            public List<string> Missing { get; } = new();
        }

        static int RunCli(string[] args)
        {
            var rootDir = args.Length > 1 ? args[1] : string.Empty;
            string? outPath = null;
            for (var i = 2; i < args.Length; i++)
            {
                if (args[i] == "--out" && i + 1 < args.Length) outPath = args[++i];
            }

            try
            {
                if (string.IsNullOrEmpty(rootDir) || rootDir.StartsWith("--"))
                    return WriteJson(outPath, new { error = $"usage: {Verb} \"<hostRootDir>\" [--out <json>]" }, 2);

                var result = Deploy(rootDir);
                return WriteJson(outPath, new
                {
                    verb = Verb,
                    root = rootDir,
                    folder = FolderName,
                    written = result.Written,
                    missing = result.Missing,
                }, 0);
            }
            catch (Exception ex)
            {
                return WriteJson(outPath, new { error = ex.ToString() }, 2);
            }
        }

        /// <summary>
        /// rootDir/ClaudeCodeForDeveloper/_specs/ を作り直して仕様書を書き出す。_specs/ 以外には触らない。
        /// </summary>
        public static DeployResult Deploy(string rootDir)
        {
            var result = new DeployResult();
            var folder = Path.Combine(rootDir, FolderName);
            var specs = Path.Combine(folder, SpecsDirName);

            if (Directory.Exists(specs)) Directory.Delete(specs, recursive: true);
            Directory.CreateDirectory(specs);

            foreach (var id in SpecIds)
            {
                var markdown = SpecDocCatalog.Load(id);
                if (markdown == null)
                {
                    result.Missing.Add(id);
                    continue;
                }
                var path = Path.Combine(specs, id + ".md");
                File.WriteAllText(path, markdown, new UTF8Encoding(false));
                result.Written.Add($"{FolderName}/{SpecsDirName}/{id}.md");
            }
            return result;
        }

        static int WriteJson(string? outPath, object payload, int exitCode)
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            if (!string.IsNullOrEmpty(outPath))
                File.WriteAllText(outPath, json, new UTF8Encoding(false));
            else
                Console.Out.WriteLine(json);
            return exitCode;
        }
    }
}

using Codeer.LowCode.Blazor.Designer.Extensibility;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Designer.Standard
{
    /// <summary>
    /// ホストソリューション (C#) のルートで Claude Code を使うための生成物を書き出す。
    ///
    /// 置き場は &lt;root&gt;/ClaudeCodeForDeveloper/。このフォルダはホスト側 (Codeer.LowCode.Blazor.Starter 等) の
    /// 静的な文書 (セットアップ手順書など・コミット対象) と、この verb が生成するもの (パッケージの版に追従する・
    /// 再生成可能) が同居する。**この verb がフォルダ内で所有するのは `_` で始まるエントリだけ**
    /// (`_specs/` = ホスト開発に関わる仕様書、`_hooks/` = ルート用フックスクリプト)。それ以外のファイルには触らない
    /// (フォルダを丸ごと作り直す ClaudeCodeForDesigner/ とは契約が違う)。
    ///
    /// 加えて、ルートの `.claude/settings.local.json` (マシン固有の許可リストとフック。exe パスを焼き込む) を
    /// **無いときだけ**生成する (claude-workspace がデザイン側でやっているのと同じ扱い。既存は触らない)。
    /// これでルートで起動した Claude Code からも、デザインプロジェクトのフォルダで起動したときと同じ許可・
    /// 自動更新 (DesignProjects/*/ の各ワークスペースの refresh-ai-workspace.ps1 を巡回) が効く。
    ///
    /// デザインプロジェクトが無くても実行できる (プロジェクトを開いて生成するカタログ類は
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
        const string HooksDirName = "_hooks";
        const string HookScriptName = "refresh-design-workspaces.ps1";
        const string ResourcePrefix = "Codeer.LowCode.Blazor.Designer.Standard.DeveloperWorkspace.";
        const string ExePlaceholder = "<デザイナexeのパス>";

        // ホスト開発 (C#) に関わる仕様書の id (SpecDocCatalog の id = _specs/<id>.md)。
        // HostCustomization が参照している _FieldCommon / ScriptExtensions も一緒に出す。
        static readonly string[] SpecIds = { "HostCustomization", "_FieldCommon", "ScriptExtensions" };

        /// <summary>headless CLI に developer-workspace verb を登録する (base.OnStartup 前に呼ぶ)。</summary>
        public static void RegisterCli() => HeadlessCliVerbs.Register(Verb, RunCli);

        public class DeployResult
        {
            /// <summary>書き出した生成物 (ホストルート相対)。毎回上書き。</summary>
            public List<string> Written { get; } = new();
            /// <summary>今回新規生成したユーザー所有ファイル (.claude/settings.local.json)。</summary>
            public List<string> Created { get; } = new();
            /// <summary>既存のため触らなかったユーザー所有ファイル。</summary>
            public List<string> Preserved { get; } = new();
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
                    created = result.Created,
                    preserved = result.Preserved,
                    missing = result.Missing,
                }, 0);
            }
            catch (Exception ex)
            {
                return WriteJson(outPath, new { error = ex.ToString() }, 2);
            }
        }

        /// <summary>
        /// rootDir/ClaudeCodeForDeveloper/{_specs,_hooks}/ を作り直し、rootDir/.claude/settings.local.json を無ければ生成する。
        /// それ以外には触らない。
        /// </summary>
        public static DeployResult Deploy(string rootDir)
        {
            var result = new DeployResult();
            var exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("designer exe path could not be resolved");
            var folder = Path.Combine(rootDir, FolderName);

            // _specs/: ホスト開発に関わる仕様書 (ライブラリ埋め込み。版に追従)
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
                File.WriteAllText(Path.Combine(specs, id + ".md"), markdown, new UTF8Encoding(false));
                result.Written.Add($"{FolderName}/{SpecsDirName}/{id}.md");
            }

            // _hooks/: ルート用フック (DesignProjects/*/ の各ワークスペースの refresh-ai-workspace.ps1 を巡回)
            var hooks = Path.Combine(folder, HooksDirName);
            if (Directory.Exists(hooks)) Directory.Delete(hooks, recursive: true);
            Directory.CreateDirectory(hooks);
            File.WriteAllText(Path.Combine(hooks, HookScriptName), LoadResource(HookScriptName), new UTF8Encoding(true));
            result.Written.Add($"{FolderName}/{HooksDirName}/{HookScriptName}");

            // .claude/settings.local.json: マシン固有 (exe パス焼き込み)。無いときだけ生成、既存は触らない
            var settingsLocal = Path.Combine(rootDir, ".claude", "settings.local.json");
            if (File.Exists(settingsLocal))
            {
                result.Preserved.Add(".claude/settings.local.json");
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsLocal)!);
                // JSON 文字列内に入るため \ をエスケープする
                var json = LoadResource("settings.local.json.sample").Replace(ExePlaceholder, exePath.Replace("\\", "\\\\"));
                File.WriteAllText(settingsLocal, json, new UTF8Encoding(false));
                result.Created.Add(".claude/settings.local.json");
            }

            return result;
        }

        static string LoadResource(string name)
        {
            using var stream = typeof(DeveloperWorkspaceDeploy).Assembly.GetManifestResourceStream(ResourcePrefix + name)
                ?? throw new InvalidOperationException($"embedded resource not found: {ResourcePrefix + name}");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
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

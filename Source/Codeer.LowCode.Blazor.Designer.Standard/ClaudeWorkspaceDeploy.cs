using Codeer.LowCode.Blazor.Designer.Extensibility;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Designer.Standard
{
    /// <summary>
    /// Claude Code ワークスペース (CLAUDE.md / ClaudeCodeForDesigner の Docs / .claude フック一式) を
    /// デザイナから展開・更新する。正本はこのライブラリの埋め込みリソース (ClaudeWorkspace.zip =
    /// ClaudeWorkspace/ フォルダをビルド時に zip 化したもの) で、展開した瞬間のドキュメントは
    /// 常にこの exe と同一バージョンになる。
    ///
    /// headless CLI:
    ///   <designer.exe> claude-workspace "<workspaceDir>" [--project <デザインプロジェクトのフォルダ名>] [--out "<resultJsonPath>"]
    ///   終了コード: 0 = 成功 / 2 = 失敗。
    ///   verb 登録 (<see cref="RegisterCli"/>) はアプリの OnStartup で base.OnStartup(e) より前に行うこと。
    ///
    /// 上書き規則:
    ///   - フレームワーク所有 (zip の中身 = CLAUDE.md / ClaudeCodeForDesigner/ / .claude のフックと共通設定 /
    ///     Project.md.sample) は毎回全上書き (更新)。
    ///   - ユーザー所有 (Project.md / ClaudeCodeForDesigner/LocalEnvironment.md / .claude/settings.local.json /
    ///     temporary/ / ddl/ / tools/) は上書きしない。無ければ生成する (exe パスを焼き込む)。
    /// </summary>
    public static class ClaudeWorkspaceDeploy
    {
        public const string Verb = "claude-workspace";
        const string ZipResourceName = "Codeer.LowCode.Blazor.Designer.Standard.ClaudeWorkspace.zip";
        const string ExePlaceholder = "<デザイナexeのパス>";
        const string ProjectPlaceholder = "<デザインプロジェクトのフォルダ>";

        /// <summary>headless CLI に claude-workspace verb を登録する (base.OnStartup 前に呼ぶ)。</summary>
        public static void RegisterCli() => HeadlessCliVerbs.Register(Verb, RunCli);

        public class DeployResult
        {
            /// <summary>上書き展開したフレームワーク所有ファイル (ワークスペース相対)。</summary>
            public List<string> Deployed { get; } = new();
            /// <summary>今回新規生成したユーザー所有ファイル。</summary>
            public List<string> Created { get; } = new();
            /// <summary>既存のため触らなかったユーザー所有ファイル。</summary>
            public List<string> Preserved { get; } = new();
        }

        static int RunCli(string[] args)
        {
            var workspaceDir = args.Length > 1 ? args[1] : string.Empty;
            string? outPath = null;
            var project = "Design";
            for (var i = 2; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out" when i + 1 < args.Length: outPath = args[++i]; break;
                    case "--project" when i + 1 < args.Length: project = args[++i]; break;
                }
            }

            try
            {
                if (string.IsNullOrEmpty(workspaceDir))
                    return WriteJson(outPath, new { error = "usage: claude-workspace \"<workspaceDir>\" [--project <folder>] [--out <json>]" }, 2);

                var result = Deploy(workspaceDir, project);
                return WriteJson(outPath, new
                {
                    verb = Verb,
                    workspace = workspaceDir,
                    project,
                    deployedCount = result.Deployed.Count,
                    created = result.Created,
                    preserved = result.Preserved,
                }, 0);
            }
            catch (Exception ex)
            {
                return WriteJson(outPath, new { error = ex.ToString() }, 2);
            }
        }

        /// <summary>
        /// ワークスペースを workspaceDir に展開・更新する。
        /// projectFolderName はワークスペースから見たデザインプロジェクトのフォルダ名 (フックが designcheck 等を叩く対象)。
        /// </summary>
        public static DeployResult Deploy(string workspaceDir, string projectFolderName = "Design")
        {
            var result = new DeployResult();
            var exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("designer exe path could not be resolved");

            Directory.CreateDirectory(workspaceDir);

            // フレームワーク所有分 (zip の中身) を全上書きで展開
            using (var stream = typeof(ClaudeWorkspaceDeploy).Assembly.GetManifestResourceStream(ZipResourceName)
                       ?? throw new InvalidOperationException($"embedded resource not found: {ZipResourceName}"))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var fullRoot = Path.GetFullPath(workspaceDir);
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // フォルダエントリ
                    var target = Path.GetFullPath(Path.Combine(workspaceDir, entry.FullName));
                    if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"invalid zip entry path: {entry.FullName}");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                    result.Deployed.Add(entry.FullName);
                }
            }

            // ユーザー所有のフォルダ (無ければ作る)
            Directory.CreateDirectory(Path.Combine(workspaceDir, "temporary"));
            Directory.CreateDirectory(Path.Combine(workspaceDir, "ddl"));

            // Project.md: プロジェクト固有ルールの置き場。無ければサンプルから生成
            var projectMd = Path.Combine(workspaceDir, "Project.md");
            var projectMdSample = Path.Combine(workspaceDir, "Project.md.sample");
            if (File.Exists(projectMd))
            {
                result.Preserved.Add("Project.md");
            }
            else if (File.Exists(projectMdSample))
            {
                File.Copy(projectMdSample, projectMd);
                result.Created.Add("Project.md");
            }

            // LocalEnvironment.md: マシン固有情報。DesignerExePath 行だけはこの exe のパスに保つ
            var localEnv = Path.Combine(workspaceDir, "ClaudeCodeForDesigner", "LocalEnvironment.md");
            UpdateLocalEnvironment(localEnv, exePath, result);

            // .claude/settings.local.json: マシン固有の許可とフック。無ければサンプルに exe パスを焼き込んで生成。
            // 既存なら触らない (ユーザーが許可を追加している可能性があるため)。exe パスが変わったときは
            // ファイルを消して再実行するか手で直す。
            var settingsLocal = Path.Combine(workspaceDir, ".claude", "settings.local.json");
            var settingsSample = Path.Combine(workspaceDir, ".claude", "settings.local.json.sample");
            if (File.Exists(settingsLocal))
            {
                result.Preserved.Add(".claude/settings.local.json");
            }
            else if (File.Exists(settingsSample))
            {
                // JSON 文字列内に入るため \ をエスケープする
                var escapedExe = exePath.Replace("\\", "\\\\");
                var json = File.ReadAllText(settingsSample, Encoding.UTF8)
                    .Replace(ExePlaceholder, escapedExe)
                    .Replace(ProjectPlaceholder, projectFolderName.Replace("\\", "\\\\"));
                File.WriteAllText(settingsLocal, json, new UTF8Encoding(false));
                result.Created.Add(".claude/settings.local.json");
            }

            return result;
        }

        // LocalEnvironment.md の DesignerExePath 行を生成・更新する (他の行はユーザー所有として保持)。
        static void UpdateLocalEnvironment(string path, string exePath, DeployResult result)
        {
            const string key = "DesignerExePath:";
            var line = $"{key} {exePath}";

            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path,
                    "# LocalEnvironment (マシン固有・配布物に含めない / gitignore 対象)\n" +
                    "\n" +
                    "このファイルはマシンローカルの設定を記録する。CLAUDE.md には書かない (配布されるため)。\n" +
                    "\n" +
                    line + "\n",
                    new UTF8Encoding(false));
                result.Created.Add("ClaudeCodeForDesigner/LocalEnvironment.md");
                return;
            }

            var lines = File.ReadAllLines(path, Encoding.UTF8).ToList();
            var index = lines.FindIndex(l => l.TrimStart().StartsWith(key, StringComparison.Ordinal));
            if (index >= 0)
            {
                if (lines[index].Trim() == line)
                {
                    result.Preserved.Add("ClaudeCodeForDesigner/LocalEnvironment.md");
                    return;
                }
                lines[index] = line;
            }
            else
            {
                lines.Add(line);
            }
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            result.Created.Add("ClaudeCodeForDesigner/LocalEnvironment.md (DesignerExePath 更新)");
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

using Codeer.LowCode.Blazor.Designer.Extensibility;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Designer.Standard
{
    /// <summary>
    /// Claude Code ワークスペースをデザイナから展開・更新する。
    ///
    /// 所有権はフォルダ単位で分ける:
    ///   - フレームワーク所有 = ClaudeCodeForDesigner/ (Docs + ai-refresh の生成物一式) と
    ///     ルートの CLAUDE.md / .claude のフックと共通設定。
    ///     ClaudeCodeForDesigner/ は展開のたびに丸ごと削除して作り直す (旧版の残骸を残さない)。
    ///     正本はこのライブラリの埋め込みリソース (ClaudeWorkspace.zip = ClaudeWorkspace/ フォルダを
    ///     ビルド時に zip 化したもの) + この exe の ai-refresh 生成物で、展開した瞬間の内容は
    ///     常にこの exe と同一バージョンになる。
    ///   - ユーザー所有 = Project.md / LocalEnvironment.md / .gitignore / .claude/settings.local.json /
    ///     ddl/ / tools/。上書きしない。無ければ生成する (Project.md は zip 内の雛形から、
    ///     exe パスは LocalEnvironment.md / settings.local.json に焼き込む)。
    ///
    /// 展開の流れ: ClaudeCodeForDesigner/ 削除 → zip 展開 → ユーザー所有ファイル生成 (無い場合のみ)
    ///   → デザインプロジェクトがあれば自 exe を ai-refresh で子プロセス起動し、生成物
    ///   (_field_catalog.md / _script_catalog.md / _defaults/ / _specs/ / _samples/) を
    ///   ClaudeCodeForDesigner/ に出力 → 成功時にバイナリ署名を _ai_refresh.stamp に記録。
    ///   フック (.claude/refresh-ai-workspace.ps1) は署名が変わったとき (= デザイナや拡張ライブラリの
    ///   再ビルド・更新) にこの verb を叩き直し、フレームワーク所有分を丸ごと最新化する。
    ///
    /// headless CLI:
    ///   <designer.exe> claude-workspace "<workspaceDir>" [--project <デザインプロジェクトのフォルダ名>] [--out "<resultJsonPath>"]
    ///   終了コード: 0 = 成功 / 2 = 失敗。
    ///   verb 登録 (<see cref="RegisterCli"/>) はアプリの OnStartup で base.OnStartup(e) より前に行うこと。
    /// </summary>
    public static class ClaudeWorkspaceDeploy
    {
        public const string Verb = "claude-workspace";
        const string ZipResourceName = "Codeer.LowCode.Blazor.Designer.Standard.ClaudeWorkspace.zip";
        const string ExePlaceholder = "<デザイナexeのパス>";
        const string ProjectPlaceholder = "<デザインプロジェクトのフォルダ>";
        const string FrameworkDirName = "ClaudeCodeForDesigner";
        const string StampFileName = "_ai_refresh.stamp";

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
            /// <summary>ai-refresh (生成リファレンス一式) の結果。ok / skipped: 理由 / failed: 理由。</summary>
            public string AiRefresh { get; set; } = "";
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
                    aiRefresh = result.AiRefresh,
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

            MigrateOldLayout(workspaceDir);

            // フレームワーク所有 = ClaudeCodeForDesigner/ は丸ごと作り直す (旧版で消えたファイルを残さない)
            var frameworkDir = Path.Combine(workspaceDir, FrameworkDirName);
            if (Directory.Exists(frameworkDir)) Directory.Delete(frameworkDir, recursive: true);

            // フレームワーク所有分 (zip の中身) を展開
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

                    // Project.md はユーザー所有 (zip の中身は雛形)。既存なら上書きしない
                    if (entry.FullName == "Project.md" && File.Exists(target))
                    {
                        result.Preserved.Add(entry.FullName);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                    if (entry.FullName == "Project.md") result.Created.Add(entry.FullName);
                    else result.Deployed.Add(entry.FullName);
                }
            }

            // ユーザー所有のフォルダ (無ければ作る)
            Directory.CreateDirectory(Path.Combine(workspaceDir, "ddl"));

            // LocalEnvironment.md: マシン固有情報。DesignerExePath 行だけはこの exe のパスに保つ
            UpdateLocalEnvironment(Path.Combine(workspaceDir, "LocalEnvironment.md"), exePath, result);

            // .gitignore: フレームワーク所有 (再生成可能) とマシン固有をコミット対象外に。
            // ユーザーが編集している可能性があるため、無い場合のみ生成する
            var gitignore = Path.Combine(workspaceDir, ".gitignore");
            if (File.Exists(gitignore))
            {
                result.Preserved.Add(".gitignore");
            }
            else
            {
                File.WriteAllText(gitignore,
                    "# デザイナ (claude-workspace / ai-refresh) が再生成するもの・マシン固有のもの\n" +
                    "ClaudeCodeForDesigner/\n" +
                    "tools/\n" +
                    "LocalEnvironment.md\n" +
                    ".claude/settings.local.json\n",
                    new UTF8Encoding(false));
                result.Created.Add(".gitignore");
            }

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

            // 生成リファレンス一式 (ai-refresh) も同じタイミングで出す。
            // デザインプロジェクトがまだ無ければスキップ (フックがプロジェクト設置後に再実行する)
            RunAiRefresh(workspaceDir, projectFolderName, exePath, result);

            return result;
        }

        // 旧レイアウト (LocalEnvironment.md が ClaudeCodeForDesigner/ 内・生成物が temporary/ 内・
        // Project.md.sample 配布) からの移行。一度移行したら以後は何もしない
        static void MigrateOldLayout(string workspaceDir)
        {
            var oldLocalEnv = Path.Combine(workspaceDir, FrameworkDirName, "LocalEnvironment.md");
            var newLocalEnv = Path.Combine(workspaceDir, "LocalEnvironment.md");
            if (File.Exists(oldLocalEnv) && !File.Exists(newLocalEnv))
                File.Move(oldLocalEnv, newLocalEnv);

            foreach (var name in new[] { "Project.md.sample", "README.md" })
            {
                var path = Path.Combine(workspaceDir, name);
                if (File.Exists(path)) File.Delete(path);
            }

            var temporary = Path.Combine(workspaceDir, "temporary");
            foreach (var name in new[] { "_field_catalog.md", "_script_catalog.md", "_ai_refresh.stamp" })
            {
                var path = Path.Combine(temporary, name);
                if (File.Exists(path)) File.Delete(path);
            }
            foreach (var name in new[] { "_defaults", "_specs", "_samples" })
            {
                var path = Path.Combine(temporary, name);
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            // 旧 deploy が作った空の temporary/ は片付ける (中身があればユーザーの作業物なので触らない)
            if (Directory.Exists(temporary) && !Directory.EnumerateFileSystemEntries(temporary).Any())
                Directory.Delete(temporary);
        }

        // ai-refresh を自 exe の子プロセスで実行し、生成物を ClaudeCodeForDesigner/ に出力する。
        // GUI プロセス内で直接プロジェクトを開くと開いている編集状態と衝突するため、必ず子プロセスにする。
        // 成功時のみバイナリ署名スタンプを書く (フックはこれで再実行の要否を判定する)
        static void RunAiRefresh(string workspaceDir, string projectFolderName, string exePath, DeployResult result)
        {
            var projectDir = Path.GetFullPath(Path.Combine(workspaceDir, projectFolderName));
            if (!File.Exists(Path.Combine(projectDir, "app.clprj")))
            {
                result.AiRefresh = $"skipped: design project not found ({projectFolderName})";
                return;
            }

            var frameworkDir = Path.Combine(workspaceDir, FrameworkDirName);
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("ai-refresh");
            psi.ArgumentList.Add(projectDir);
            psi.ArgumentList.Add("--out-dir");
            psi.ArgumentList.Add(frameworkDir);

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("failed to start ai-refresh process");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                File.WriteAllText(Path.Combine(frameworkDir, StampFileName),
                    ComputeBinarySignature(exePath), new UTF8Encoding(false));
                result.AiRefresh = "ok";
            }
            else
            {
                var detail = (stderr.Result + stdout.Result).Trim();
                result.AiRefresh = $"failed: ai-refresh exit {process.ExitCode} {detail}";
            }
        }

        // バイナリ署名 = exe と同フォルダの *.dll の LastWriteTimeUtc.Ticks の最大値。
        // .claude/refresh-ai-workspace.ps1 と同一定義であること (フックが照合する)
        static string ComputeBinarySignature(string exePath)
        {
            var dir = Path.GetDirectoryName(exePath)!;
            var sig = File.GetLastWriteTimeUtc(exePath).Ticks;
            foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
            {
                var ticks = File.GetLastWriteTimeUtc(dll).Ticks;
                if (ticks > sig) sig = ticks;
            }
            return sig.ToString(CultureInfo.InvariantCulture);
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
                result.Created.Add("LocalEnvironment.md");
                return;
            }

            var lines = File.ReadAllLines(path, Encoding.UTF8).ToList();
            var index = lines.FindIndex(l => l.TrimStart().StartsWith(key, StringComparison.Ordinal));
            if (index >= 0)
            {
                if (lines[index].Trim() == line)
                {
                    result.Preserved.Add("LocalEnvironment.md");
                    return;
                }
                lines[index] = line;
            }
            else
            {
                lines.Add(line);
            }
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            result.Created.Add("LocalEnvironment.md (DesignerExePath 更新)");
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

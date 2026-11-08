using Codeer.LowCode.Blazor.Designer.Extensibility;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codeer.LowCode.Blazor.Designer.Standard
{
    /// <summary>
    /// ホストソリューション (C#) のルートで AI エージェント (Claude Code / Codex / GitHub Copilot) を使うための生成物を書き出す。
    ///
    /// 置き場は &lt;root&gt;/AIForDeveloper/。このフォルダはホスト側 (Codeer.LowCode.Blazor.Starter 等) が持つ
    /// 静的なファイル (セットアップ手順書・ルート用フックスクリプト hooks/・許可とフックの雛形 settings.local.json.sample。
    /// コミット対象) と、この verb が生成するもの (パッケージの版に追従する・再生成可能) が同居する。
    /// **この verb がフォルダ内で所有するのは `_` で始まるエントリだけ** (`_specs/` = ホスト開発に関わる仕様書)。
    /// それ以外のファイルには触らない (フォルダを丸ごと作り直す AIForDesigner/ とは契約が違う)。
    ///
    /// 加えて、ホストが持つ雛形 AIForDeveloper/settings.local.json.sample にこの exe のパスを焼き込み、
    /// ルートの `.claude/settings.local.json` (マシン固有の許可リストとフック) を生成・更新する
    /// (ai-workspace がデザイン側でやっているのと同じ扱い: 無ければ生成、あれば hooks 節とサンプル由来の CLI 許可だけを
    /// この exe に合わせて更新し、ユーザーが足した許可は保持する。<see cref="AgentSettingsFile"/>)。
    /// これでルートで起動したエージェントからも、デザインプロジェクトのフォルダで起動したときと同じ許可・
    /// 自動更新 (DesignProjects/*/ の各ワークスペースの refresh-ai-workspace.ps1 を巡回) が効く。
    /// GitHub Copilot は同じファイルのフックを読む。Codex 用には同じ内容の .codex/hooks.json と
    /// .codex/rules/codeer-lowcode.rules を毎回作り直す (中身が同じなら書かない)。
    ///
    /// 雛形が無い (ホストのテンプレートが古い) ときは失敗にする (埋め込みの予備は持たない。雛形の正本はホスト側 1 つ)。
    /// 旧版のこの verb が生成していた _hooks/ は、雛形がホスト側 (hooks/) に移ったので片付ける。
    /// フォルダの旧名 ClaudeCodeForDeveloper/ (Designer.Standard 0.8.25 まで) が残っていれば、生成物 (`_` で始まるもの) を消し、
    /// 新フォルダに無いファイル (starter-origin.json 等) を移し、空になれば消す。残ったもの (新フォルダにも同名があるホストの文書) は結果で知らせる。
    ///
    /// デザインプロジェクトが無くても実行できる (プロジェクトを開いて生成するカタログ類は
    /// デザインプロジェクト側の AIForDesigner/ が持つ)。
    ///
    /// headless CLI:
    ///   &lt;designer.exe&gt; developer-workspace "&lt;hostRootDir&gt;" [--out "&lt;resultJsonPath&gt;"]
    ///   終了コード: 0 = 成功 / 2 = 失敗。
    ///   verb 登録 (<see cref="RegisterCli"/>) はアプリの OnStartup で base.OnStartup(e) より前に行うこと。
    /// </summary>
    public static class DeveloperWorkspaceDeploy
    {
        public const string Verb = "developer-workspace";
        public const string FolderName = "AIForDeveloper";
        // 旧名 (Designer.Standard 0.8.25 まで)。残っていれば生成物を消し、新フォルダに無いファイルを移す
        public const string OldFolderName = "ClaudeCodeForDeveloper";
        const string SpecsDirName = "_specs";
        const string SettingsSampleName = "settings.local.json.sample";
        const string ExePlaceholder = "<デザイナexeのパス>";
        // 旧版のこの verb が生成していたルート用フックの置き場。雛形がホスト側 (hooks/) に移ったので片付ける
        const string OldHooksDirName = "_hooks";

        // ホスト開発 (C#) に関わる仕様書の id (SpecDocCatalog の id = _specs/<id>.md)。
        // HostCustomization が参照している _FieldCommon / ScriptExtensions も一緒に出す。
        static readonly string[] SpecIds = { "HostCustomization", "_FieldCommon", "ScriptExtensions" };

        /// <summary>headless CLI に developer-workspace verb を登録する (base.OnStartup 前に呼ぶ)。</summary>
        public static void RegisterCli() => HeadlessCliVerbs.Register(Verb, RunCli);

        public class DeployResult
        {
            /// <summary>書き出した生成物 (ホストルート相対)。毎回上書き。</summary>
            public List<string> Written { get; } = new();
            /// <summary>今回新規生成・更新したユーザー所有ファイル (.claude/settings.local.json)。</summary>
            public List<string> Created { get; } = new();
            /// <summary>既存のため触らなかった (変更の無かった) ファイル。</summary>
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

                // 雛形が無い = ホストのテンプレートが古い。利用者向けの短い理由だけ返す
                var missingSample = GetMissingSampleMessage(rootDir);
                if (missingSample != null) return WriteJson(outPath, new { error = missingSample }, 2);

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
        /// 雛形 (rootDir/AIForDeveloper/settings.local.json.sample) が無いときの利用者向けの理由。あれば null。
        /// </summary>
        public static string? GetMissingSampleMessage(string rootDir)
            => File.Exists(Path.Combine(rootDir, FolderName, SettingsSampleName)) ? null :
                $"{FolderName}/{SettingsSampleName} が見つからない。ホストのテンプレートが古い。" +
                $"{FolderName}/claude-code-update.md の手順で最新のテンプレートの文書 (settings.local.json.sample と hooks/) を取り込んでから実行する";

        /// <summary>
        /// rootDir/AIForDeveloper/_specs/ を作り直し、rootDir/.claude/settings.local.json と .codex/ を
        /// ホストの雛形 (AIForDeveloper/settings.local.json.sample) から生成・更新する。それ以外には触らない。
        /// 雛形が無ければ FileNotFoundException。
        /// </summary>
        public static DeployResult Deploy(string rootDir)
        {
            var result = new DeployResult();
            var exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("designer exe path could not be resolved");
            var folder = Path.Combine(rootDir, FolderName);

            // 雛形 (ホスト所有)。無ければ何も変えずに失敗
            var samplePath = Path.Combine(folder, SettingsSampleName);
            if (GetMissingSampleMessage(rootDir) is { } missingSample) throw new FileNotFoundException(missingSample, samplePath);

            MigrateOldFolder(rootDir, folder, result);

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

            // 旧版が生成していた _hooks/ を片付ける (フックの正本はホストの hooks/ に移った。settings.local.json の hooks 節は下で新しい雛形に揃う)
            var oldHooks = Path.Combine(folder, OldHooksDirName);
            if (Directory.Exists(oldHooks)) Directory.Delete(oldHooks, recursive: true);

            // .claude/settings.local.json: マシン固有 (exe パス焼き込み)。無ければ生成、あれば hooks 節と CLI 許可を更新
            // JSON 文字列内に入るため \ をエスケープする
            var json = File.ReadAllText(samplePath, Encoding.UTF8).Replace(ExePlaceholder, exePath.Replace("\\", "\\\\"));
            AgentSettingsFile.CreateOrUpdate(Path.Combine(rootDir, ".claude", "settings.local.json"), json,
                result.Created, result.Preserved, ".claude/settings.local.json");

            // Codex 用 (.codex/hooks.json・.codex/rules/codeer-lowcode.rules): 同じ雛形から毎回作り直す (中身が同じなら書かない)
            if (JsonNode.Parse(json) is JsonObject sample)
                CodexWorkspaceFiles.Write(rootDir, sample, overwrite: true, result.Written, result.Preserved);

            return result;
        }

        // 旧名のフォルダ (ClaudeCodeForDeveloper/) の片付け: 生成物 (`_` で始まるもの) は消し、新フォルダに無いファイルは移す
        // (starter-origin.json 等。ホストの最新化手順が新しい文書を新フォルダに置いた後に呼ばれる想定)。空になれば消す。
        // 新フォルダにも同名があるもの (旧い文書) は触らず、残っていることを結果で知らせる
        static void MigrateOldFolder(string rootDir, string folder, DeployResult result)
        {
            var oldFolder = Path.Combine(rootDir, OldFolderName);
            if (!Directory.Exists(oldFolder)) return;

            foreach (var dir in Directory.GetDirectories(oldFolder))
                if (Path.GetFileName(dir).StartsWith("_")) Directory.Delete(dir, recursive: true);

            var moved = new List<string>();
            var left = new List<string>();
            foreach (var file in Directory.GetFiles(oldFolder, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(oldFolder, file);
                var dest = Path.Combine(folder, rel);
                if (File.Exists(dest))
                {
                    left.Add(rel.Replace('\\', '/'));
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Move(file, dest);
                moved.Add(rel.Replace('\\', '/'));
            }
            if (!Directory.EnumerateFileSystemEntries(oldFolder, "*", SearchOption.AllDirectories).Any())
                Directory.Delete(oldFolder, recursive: true);

            if (moved.Count > 0) result.Created.Add($"{FolderName}/ ({OldFolderName}/ から移動: {string.Join(", ", moved)})");
            if (left.Count > 0) result.Preserved.Add($"{OldFolderName}/ (旧い文書が残っている。{FolderName}/ に同じものがあるので消してよい: {string.Join(", ", left)})");
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

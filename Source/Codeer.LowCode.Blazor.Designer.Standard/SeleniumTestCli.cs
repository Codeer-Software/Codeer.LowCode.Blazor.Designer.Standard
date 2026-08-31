using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.Designer.Standard.SeleniumPageObject;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codeer.LowCode.Blazor.Designer.Standard
{
    /// <summary>
    /// Selenium テスト用の headless verb。
    ///
    /// selenium-test-init:
    ///   <designer.exe> selenium-test-init --out-dir "<testProjectDir>" [--name <AppName>] [--project "<designDir>"] [--base-url <url>] [--out "<json>"]
    ///   テストプロジェクトの雛形 (csproj / WebDriverManager / DataManager / LoginForm / SmokeTest ...) を --out-dir に展開する。
    ///   --out-dir は空か存在しないフォルダのみ。--name (既定 LowCodeApp) でファイル名・名前空間の "LowCodeApp" を置き換える。
    ///   --project を渡すと designer.settings.json の DataSources を testsettings.json に、designer.settings.Development.json の
    ///   接続文字列を testsettings.local.json (gitignore) に写し、app.clprj の CurrentUserModuleDesignName が空なら Login を無効にする
    ///   (秘密情報はこの exe が直接ファイル間で移すだけで、呼び出し側 (AI) には渡らない)。
    ///
    /// pageobject:
    ///   <designer.exe> pageobject "<designDir>" --out-dir "<dir>" [--namespace <ns>] [--out "<json>"]
    ///   デザインから Selenium PageObject (C#) を生成する (GUI の Tools > Export PageObject と同じ生成器)。
    ///   --out-dir 配下の *.cs は生成物として全部作り直す (消えたモジュールの残骸を残さない)。
    /// </summary>
    public static class SeleniumTestCli
    {
        public const string InitVerb = "selenium-test-init";
        public const string PageObjectVerb = "pageobject";
        const string TemplateZipResourceName = "Codeer.LowCode.Blazor.Designer.Standard.SeleniumTestTemplate.zip";
        const string TemplateAppName = "LowCodeApp";

        public static void RegisterCli()
        {
            HeadlessCliVerbs.Register(InitVerb, RunInit);
            HeadlessCliVerbs.Register(PageObjectVerb, RunPageObject);
        }

        static int RunInit(string[] args)
        {
            string? outPath = null, outDir = null, projectDir = null, baseUrl = null;
            var name = TemplateAppName;
            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out" when i + 1 < args.Length: outPath = args[++i]; break;
                    case "--out-dir" when i + 1 < args.Length: outDir = args[++i]; break;
                    case "--name" when i + 1 < args.Length: name = args[++i]; break;
                    case "--project" when i + 1 < args.Length: projectDir = args[++i]; break;
                    case "--base-url" when i + 1 < args.Length: baseUrl = args[++i]; break;
                }
            }

            try
            {
                if (string.IsNullOrEmpty(outDir))
                    return WriteJson(outPath, new { error = "--out-dir is required" }, 2);
                if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any())
                    return WriteJson(outPath, new { error = $"--out-dir must be empty or not exist: {outDir}" }, 2);
                if (!IsValidIdentifier(name))
                    return WriteJson(outPath, new { error = $"--name must be a C# identifier (letters, digits, '_' and '.'): {name}" }, 2);

                outDir = Path.GetFullPath(outDir);
                Directory.CreateDirectory(outDir);
                var created = ExtractTemplate(outDir, name);

                var settingsPath = Path.Combine(outDir, "testsettings.json");
                var settings = LoadJson(settingsPath);
                var localPath = Path.Combine(outDir, "testsettings.local.json");
                var local = new JsonObject();
                var dataSourceNames = new List<string>();
                bool? loginEnabled = null;

                if (!string.IsNullOrEmpty(projectDir))
                {
                    projectDir = Path.GetFullPath(projectDir);
                    if (!File.Exists(Path.Combine(projectDir, "app.clprj")))
                        return WriteJson(outPath, new { error = $"design project not found (app.clprj): {projectDir}" }, 2);

                    // DataSources (名前と種別。秘密なし) → testsettings.json
                    var designerSettings = LoadJson(Path.Combine(projectDir, "designer.settings.json"));
                    if (designerSettings["DataSources"] is JsonArray dataSources)
                    {
                        var copied = new JsonArray();
                        foreach (var ds in dataSources.OfType<JsonObject>())
                        {
                            var dsName = ds["Name"]?.GetValue<string>() ?? string.Empty;
                            if (string.IsNullOrEmpty(dsName)) continue;
                            copied.Add(new JsonObject { ["Name"] = dsName, ["DataSourceType"] = ds["DataSourceType"]?.DeepClone() });
                            dataSourceNames.Add(dsName);
                        }
                        settings["DataSources"] = copied;
                    }

                    // 接続文字列 (秘密) → testsettings.local.json。ファイル間で移すだけで標準出力には出さない
                    var devSettings = LoadJson(Path.Combine(projectDir, "designer.settings.Development.json"));
                    if (devSettings["ConnectionStrings"] is JsonObject connectionStrings && connectionStrings.Count > 0)
                    {
                        local["ConnectionStrings"] = connectionStrings.DeepClone();
                    }

                    // 認証の有無 → Login の有効/無効
                    var appSettings = LoadJson(Path.Combine(projectDir, "app.clprj"));
                    loginEnabled = !string.IsNullOrEmpty(appSettings["CurrentUserModuleDesignName"]?.GetValue<string>());
                    if (settings["Login"] is JsonObject login && loginEnabled == false)
                    {
                        login["UserName"] = string.Empty;
                        login["Password"] = string.Empty;
                    }
                }

                if (!string.IsNullOrEmpty(baseUrl)) settings["BaseUrl"] = baseUrl;

                SaveJson(settingsPath, settings);
                if (local.Count > 0) SaveJson(localPath, local);

                return WriteJson(outPath, new
                {
                    verb = InitVerb,
                    outDir,
                    name,
                    projectName = $"{name}.SeleniumTest",
                    createdCount = created,
                    dataSources = dataSourceNames,
                    connectionStringsCopied = local.Count > 0,
                    loginEnabled,
                    baseUrl = settings["BaseUrl"]?.GetValue<string>(),
                    next = new[]
                    {
                        "testsettings.json の BaseUrl を起動中のサーバーに合わせる",
                        $"pageobject \"<designDir>\" --out-dir \"{Path.Combine(outDir, "PageObject")}\" --namespace {name}.SeleniumTest.PageObject",
                        "dotnet test",
                    },
                }, 0);
            }
            catch (Exception ex)
            {
                return WriteJson(outPath, new { error = ex.ToString() }, 2);
            }
        }

        // 雛形 zip を展開し、"LowCodeApp" をアプリ名に置き換える (ファイル名・フォルダ名・テキスト内容)
        static int ExtractTemplate(string outDir, string name)
        {
            var count = 0;
            using var stream = typeof(SeleniumTestCli).Assembly.GetManifestResourceStream(TemplateZipResourceName)
                ?? throw new InvalidOperationException($"embedded resource not found: {TemplateZipResourceName}");
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var fullRoot = Path.GetFullPath(outDir);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                // zip のルートは "LowCodeApp.SeleniumTest/..."。その 1 段を剥がして out-dir 直下に置く
                var relative = entry.FullName.Replace('\\', '/');
                var slash = relative.IndexOf('/');
                if (slash >= 0) relative = relative.Substring(slash + 1);
                if (string.IsNullOrEmpty(relative)) continue;
                relative = relative.Replace(TemplateAppName, name);

                var target = Path.GetFullPath(Path.Combine(outDir, relative));
                if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"invalid zip entry path: {entry.FullName}");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                using var entryStream = entry.Open();
                using var reader = new StreamReader(entryStream, Encoding.UTF8);
                var text = reader.ReadToEnd().Replace(TemplateAppName, name);
                File.WriteAllText(target, text, new UTF8Encoding(false));
                count++;
            }
            return count;
        }

        static int RunPageObject(string[] args)
        {
            var projectDir = args.Length > 1 ? args[1] : string.Empty;
            string? outPath = null, outDir = null, ns = null;
            for (var i = 2; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out" when i + 1 < args.Length: outPath = args[++i]; break;
                    case "--out-dir" when i + 1 < args.Length: outDir = args[++i]; break;
                    case "--namespace" when i + 1 < args.Length: ns = args[++i]; break;
                }
            }

            try
            {
                if (string.IsNullOrEmpty(outDir))
                    return WriteJson(outPath, new { error = "--out-dir is required" }, 2);
                var designData = HeadlessDesignProject.Load(projectDir, out var error);
                if (designData == null)
                    return WriteJson(outPath, new { error }, 2);

                outDir = Path.GetFullPath(outDir);
                Directory.CreateDirectory(outDir);
                // 生成物フォルダ: 既存の *.cs を消して作り直す (モジュール削除の残骸を残さない)
                var removed = 0;
                foreach (var file in Directory.GetFiles(outDir, "*.cs"))
                {
                    File.Delete(file);
                    removed++;
                }

                new SeleniumPageObjectBuilder
                {
                    TargetPath = outDir,
                    Namespace = string.IsNullOrEmpty(ns) ? "PageObject" : ns,
                }.Build(designData);

                var files = Directory.GetFiles(outDir, "*.cs").Select(Path.GetFileName).OrderBy(x => x).ToList();
                return WriteJson(outPath, new
                {
                    verb = PageObjectVerb,
                    projectDir = Path.GetFullPath(projectDir),
                    outDir,
                    @namespace = string.IsNullOrEmpty(ns) ? "PageObject" : ns,
                    removed,
                    generated = files.Count,
                    files,
                }, 0);
            }
            catch (Exception ex)
            {
                return WriteJson(outPath, new { error = ex.ToString() }, 2);
            }
        }

        static bool IsValidIdentifier(string name)
            => !string.IsNullOrEmpty(name) && name.Split('.').All(part => part.Length > 0 && (char.IsLetter(part[0]) || part[0] == '_') && part.All(c => char.IsLetterOrDigit(c) || c == '_'));

        static JsonObject LoadJson(string path)
        {
            if (!File.Exists(path)) return new JsonObject();
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }) as JsonObject ?? new JsonObject();
        }

        static readonly JsonSerializerOptions _jsonWrite = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        static void SaveJson(string path, JsonObject node)
            => File.WriteAllText(path, node.ToJsonString(_jsonWrite), new UTF8Encoding(false));

        static int WriteJson(string? outPath, object payload, int exitCode)
        {
            var json = JsonSerializer.Serialize(payload, _jsonWrite);
            if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, json, new UTF8Encoding(false));
            else Console.Out.WriteLine(json);
            return exitCode;
        }
    }
}

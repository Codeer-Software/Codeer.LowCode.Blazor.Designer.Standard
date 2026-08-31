using System.Text.Json;
using System.Text.Json.Nodes;
using Codeer.LowCode.Blazor.SystemSettings;

namespace LowCodeApp.SeleniumTest;

/// <summary>
/// testsettings.json (共有) + testsettings.local.json (マシン固有・gitignore) を読む。
/// 同名キーは local が優先 (オブジェクトは再帰的にマージ)。
/// </summary>
public class TestSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public LoginSettings Login { get; set; } = new();
    public BrowserSettings Browser { get; set; } = new();
    public DataSource[] DataSources { get; set; } = [];
    public Dictionary<string, string> ConnectionStrings { get; set; } = new();

    public class LoginSettings
    {
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool IsEnabled => !string.IsNullOrEmpty(UserName);
    }

    public class BrowserSettings
    {
        public bool Headless { get; set; }
        public int WindowWidth { get; set; } = 1600;
        public int WindowHeight { get; set; } = 1000;
    }

    static readonly Lazy<TestSettings> _instance = new(Load);
    public static TestSettings Instance => _instance.Value;

    static TestSettings Load()
    {
        var dir = AppContext.BaseDirectory;
        var merged = ReadJson(Path.Combine(dir, "testsettings.json")) ?? new JsonObject();
        var local = ReadJson(Path.Combine(dir, "testsettings.local.json"));
        if (local != null) Merge(merged, local);

        var settings = merged.Deserialize<TestSettings>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }) ?? new TestSettings();
        // ConnectionStrings:<Name> を DataSources に流し込む (SystemConfig と同じ約束)
        foreach (var ds in settings.DataSources)
        {
            if (settings.ConnectionStrings.TryGetValue(ds.Name, out var cs)) ds.ConnectionString = cs;
        }
        // 環境変数で上書き可 (CI 用): SELENIUM_BASE_URL / SELENIUM_HEADLESS
        var baseUrl = Environment.GetEnvironmentVariable("SELENIUM_BASE_URL");
        if (!string.IsNullOrEmpty(baseUrl)) settings.BaseUrl = baseUrl;
        var headless = Environment.GetEnvironmentVariable("SELENIUM_HEADLESS");
        if (bool.TryParse(headless, out var h)) settings.Browser.Headless = h;
        return settings;
    }

    static JsonObject? ReadJson(string path)
        => File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }) as JsonObject : null;

    static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source.ToList())
        {
            if (value is JsonObject so && target[key] is JsonObject to) Merge(to, so);
            else target[key] = value?.DeepClone();
        }
    }
}

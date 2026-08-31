using System.Diagnostics;
using NUnit.Framework.Interfaces;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace LowCodeApp.SeleniumTest;

/// <summary>
/// ブラウザ (Chrome) を 1 つだけ起こして全テストで共有する。
/// 初回アクセス時に BaseUrl を開き、testsettings の Login が設定されていればログインまで済ませる。
/// 失敗したテストの後は <see cref="FailedCleanup"/> でスクリーンショットを残してブラウザを作り直す (次のテストへ影響させない)。
/// </summary>
[SetUpFixture]
public class WebDriverManager
{
    public static string BaseUrl => TestSettings.Instance.BaseUrl.TrimEnd('/');

#pragma warning disable NUnit1032
    static IWebDriver? _driver;
#pragma warning restore NUnit1032

    /// <summary>共有ブラウザ。無ければ作って BaseUrl を開き、必要ならログインする。</summary>
    public static IWebDriver Driver
    {
        get
        {
            if (_driver != null) return _driver;
            _driver = CreateDriver();
            _driver.Url = BaseUrl;
            var login = TestSettings.Instance.Login;
            if (login.IsEnabled)
            {
                _driver.AttachLoginForm().Login(login.UserName, login.Password);
            }
            WaitLoading();
            return _driver;
        }
    }

    /// <summary>ページ内の URL へ移動する ("/Main/Product" のようにルート相対で渡す)。</summary>
    public static void Navigate(string relativePath)
    {
        Driver.Url = BaseUrl + "/" + relativePath.TrimStart('/');
        WaitLoading();
    }

    /// <summary>ローディング表示 (backdrop) が消えるまで待つ。画面遷移・保存・検索の後に呼ぶ。</summary>
    public static void WaitLoading(int timeoutMilliseconds = 30000)
    {
        // 描画までのタイムラグ分を先に待つ
        Thread.Sleep(500);
        var sw = Stopwatch.StartNew();
        while (_driver != null && _driver.FindElements(By.ClassName("backdrop")).Count > 0)
        {
            if (sw.ElapsedMilliseconds > timeoutMilliseconds) throw new TimeoutException("loading did not finish");
            Thread.Sleep(50);
        }
    }

    /// <summary>
    /// 各テストの [TearDown] から呼ぶ。失敗時はスクリーンショットを TestResults に保存し、ブラウザを破棄する
    /// (次のテストが新しいブラウザ + 再ログインで始まる)。
    /// </summary>
    public static void FailedCleanup()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status != TestStatus.Failed) return;
        try
        {
            if (_driver is ITakesScreenshot shot)
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "TestResults");
                Directory.CreateDirectory(dir);
                var name = string.Join("_", TestContext.CurrentContext.Test.FullName.Split(Path.GetInvalidFileNameChars()));
                var path = Path.Combine(dir, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                shot.GetScreenshot().SaveAsFile(path);
                TestContext.AddTestAttachment(path);
            }
        }
        catch { }
        _driver?.Dispose();
        _driver = null;
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _driver?.Dispose();
        _driver = null;
        KillChromeDriver();
    }

    static IWebDriver CreateDriver()
    {
        KillChromeDriver();
        var browser = TestSettings.Instance.Browser;
        var options = new ChromeOptions();
        if (browser.Headless) options.AddArgument("--headless=new");
        options.AddArgument($"--window-size={browser.WindowWidth},{browser.WindowHeight}");
        // 自己署名の開発証明書 (dotnet dev-certs) を許容する
        options.AcceptInsecureCertificates = true;
        // Selenium Manager がインストール済み Chrome に合う chromedriver を自動取得する
        return new ChromeDriver(options);
    }

    static void KillChromeDriver()
        => Process.GetProcessesByName("chromedriver").ToList().ForEach(e => { try { e.Kill(); } catch { } });
}

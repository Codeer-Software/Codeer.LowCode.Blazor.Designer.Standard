using OpenQA.Selenium;

namespace LowCodeApp.SeleniumTest.Scenario;

/// <summary>
/// 環境確認用の最小テスト: サーバーに到達でき、(認証ありなら) ログインでき、アプリの画面が表示されること。
/// シナリオを書くときはこのファイルの形 (SetUp / TearDown / WaitLoading / PageObject) を写す。
/// </summary>
public class SmokeTest
{
    IWebDriver _driver = default!;

    [SetUp]
    public void SetUp()
    {
        _driver = WebDriverManager.Driver;
        // このテストが触るテーブルだけを初期化する例:
        // DataManager.DeleteAll("SampleSQLite", "order_details", "orders");
    }

    [TearDown]
    public void TearDown() => WebDriverManager.FailedCleanup();

    [Test]
    public void アプリが表示される()
    {
        WebDriverManager.WaitLoading();
        // Blazor アプリの本体が描画されていること (PageFrame の <main>)
        _driver.FindElements(By.CssSelector("main")).Count.Is(x => x > 0, "main element");
        // 認証ありならログイン画面のままではないこと
        if (TestSettings.Instance.Login.IsEnabled) _driver.Title.IsNot("Login");
    }
}

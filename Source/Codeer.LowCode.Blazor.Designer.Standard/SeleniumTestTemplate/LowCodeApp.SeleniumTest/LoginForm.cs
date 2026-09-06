using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;
using Selenium.StandardControls.TestAssistant.GeneratorToolKit;

namespace LowCodeApp.SeleniumTest;

/// <summary>
/// Cookie 認証テンプレートのログイン画面 (wwwroot/login.html + login.js)。
/// タイトルはアプリごとに書き換えられるので、URL (login.html) と要素の id で特定する。
/// </summary>
public class LoginForm : PageBase
{
    public TextBoxDriver Id => ById("Id").Wait();
    public TextBoxDriver Password => ById("Password").Wait();
    public ButtonDriver LoginButton => ById("LoginButton").Wait();
    /// <summary>二要素認証 (認証アプリ / メール) のコード入力。ユーザーモジュールの LoginAccountContractField で有効にしたときだけ出る。</summary>
    public TextBoxDriver TotpCode => ById("TotpCode").Wait();
    public ButtonDriver VerifyButton => ById("VerifyButton").Wait();
    public IWebElement ErrorMessage => ById("ErrorMessage").Wait().Find();
    public IWebElement Message => ByClassName("toast-message").Wait().Find();

    public void Login(string userName, string password)
    {
        Id.Edit(userName);
        Password.Edit(password);
        LoginButton.Click();
    }

    public LoginForm(IWebDriver driver) : base(driver) { }
}

public static class LoginFormExtensions
{
    [PageObjectIdentify(UrlCompareType.Contains, "login.html")]
    public static LoginForm AttachLoginForm(this IWebDriver driver)
    {
        driver.WaitForUrl(UrlCompareType.Contains, "login.html");
        return new LoginForm(driver);
    }

    /// <summary>ログアウト (ヘッダーのログアウトリンク)。</summary>
    public static void Logout(this IWebDriver driver)
    {
        var logout = driver.FindElement(By.CssSelector("[data-system='logout']"));
        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", logout);
    }
}

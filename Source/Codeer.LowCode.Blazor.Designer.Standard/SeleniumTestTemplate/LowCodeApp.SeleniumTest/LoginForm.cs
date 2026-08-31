using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;
using Selenium.StandardControls.TestAssistant.GeneratorToolKit;

namespace LowCodeApp.SeleniumTest;

/// <summary>Cookie 認証テンプレートのログイン画面 (Pages/Login.razor)。</summary>
public class LoginForm : PageBase
{
    public TextBoxDriver Id => ByCssSelector("input[type='text']").Wait();
    public TextBoxDriver Password => ByCssSelector("input[type='password']").Wait();
    public ButtonDriver LoginButton => ByTagName("button").Wait();
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
    [PageObjectIdentify(TitleCompareType.Equals, "Login")]
    public static LoginForm AttachLoginForm(this IWebDriver driver)
    {
        driver.WaitForTitle(TitleCompareType.Equals, "Login");
        return new LoginForm(driver);
    }

    /// <summary>ログアウト (ヘッダーのログアウトリンク)。</summary>
    public static void Logout(this IWebDriver driver)
    {
        var logout = driver.FindElement(By.CssSelector("[data-system='logout']"));
        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", logout);
    }
}

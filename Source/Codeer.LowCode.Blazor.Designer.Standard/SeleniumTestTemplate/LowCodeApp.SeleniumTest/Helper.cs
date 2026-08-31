using Codeer.LowCode.Blazor.SeleniumDrivers;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;

namespace LowCodeApp.SeleniumTest;

public static class Helper
{
    /// <summary>ViewOnly (読み取り専用表示) になっているか。</summary>
    public static bool IsControlViewOnly(this IWebElement element)
        => element.GetCssValue("pointer-events") == "none";

    public static void DoubleClick(this IWebElement element)
    {
        var driver = (element as IWrapsDriver)?.WrappedDriver;
        if (driver is IJavaScriptExecutor js)
        {
            js.ExecuteScript("arguments[0].scrollIntoView({block:'center', inline:'center'});", element);
        }
        new Actions(driver).MoveToElement(element).DoubleClick(element).Perform();
    }

    /// <summary>MessageBox (確認ダイアログ・完了メッセージ) のボタンを押して、ローディングが消えるまで待つ。0 = 左端 (OK / はい)。</summary>
    public static void ClickMessageBoxButton(this IWebDriver driver, int index = 0)
    {
        driver.AttachMessageBox().Buttons.GetItem(index).Click();
        WebDriverManager.WaitLoading();
    }
}

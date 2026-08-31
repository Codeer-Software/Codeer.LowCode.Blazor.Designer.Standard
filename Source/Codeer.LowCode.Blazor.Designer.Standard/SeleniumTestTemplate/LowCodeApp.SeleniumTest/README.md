# LowCodeApp.SeleniumTest

Codeer.LowCode.Blazor アプリの Selenium (NUnit) テストプロジェクト。デザイナの `selenium-test-init` サブコマンド
(または Codeer.LowCode.Blazor.Starter) が生成した雛形。**中身は自由に書き換えてよい**。

| ファイル | 役割 |
|---|---|
| `testsettings.json` | 共有設定: `BaseUrl` / `Login` / `Browser` / `DataSources`。コミットする |
| `testsettings.local.json` | マシン固有: `ConnectionStrings`。gitignore 済み。同名キーは local が優先 |
| `WebDriverManager.cs` | 共有ブラウザの生成・初回ログイン・`WaitLoading()`・失敗時スクリーンショット |
| `LoginForm.cs` | Cookie 認証テンプレートのログイン画面 PageObject |
| `DataManager.cs` | テストデータの初期化・投入・確認 (アプリと同じ DB アクセス層。DB 種別を問わない) |
| `PageObject/` | **生成物**。デザイナの `pageobject` サブコマンドで作り直す。手で編集しない |
| `Scenario/` | テスト本体。`SmokeTest.cs` を写して増やす |
| `ChainingAssertion.NUnit.cs` | `value.Is(expected)` 形式のアサーション |

## 実行

1. サーバーを起動しておく (`testsettings.json` の `BaseUrl`)
2. デザインを変えたら PageObject を作り直す:
   `LowCodeApp.Designer.exe pageobject "<Design>" --out-dir "<このフォルダ>\PageObject" --namespace LowCodeApp.SeleniumTest.PageObject`
3. `dotnet test` (Chrome が必要。chromedriver は Selenium Manager が自動取得)

環境変数 `SELENIUM_BASE_URL` / `SELENIUM_HEADLESS=true` で設定を上書きできる (CI 用)。

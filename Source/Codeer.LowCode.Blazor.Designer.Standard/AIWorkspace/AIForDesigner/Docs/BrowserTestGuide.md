# ブラウザでの動作確認（サーバー起動 → deploy → Playwright）

デザインファイルを変更したら、実際の画面で確認してから次に進む。流れは「サーバーが起きているか確かめる → 自分の編集を `deploy` で反映 → Playwright でログインして対象ページを開き、スクショと DOM を取る → 期待値と突き合わせる」。

## 1. サーバーの所在を確かめる

サーバーの扱いは 3 点: **ユーザーが起動していればそれを使う / 起動していなければ自分で起動してよい / ユーザーに「起動しないで」「止めて」と言われたらやめる**（以後そのセッションでは起動せず、自分で起動したものは止める）。

- URL は `LocalEnvironment.md` の `ServerUrl:` 行にあればそれを使う。無ければホスト側 `CLAUDE.md`「ビルドと起動」か、Server プロジェクトの `Properties/launchSettings.json`（https プロファイルの `applicationUrl`）から取り、`LocalEnvironment.md` に `ServerUrl: https://localhost:7137` の形で記録しておく（マシン固有・配布されない）
- 起きているかは、下の Playwright スクリプトで `page.goto` してみればわかる（接続拒否なら起きていない）。起きていればそれを使う
- **起きていなければ自分で起動する**（ホストソリューションと同居しているとき）。エージェントのバックグラウンド実行（Claude Code は Bash のバックグラウンド実行、Copilot は非同期の実行）で、ホスト側 `CLAUDE.md`「ビルドと起動」の `dotnet run` を流す。アプリのフォルダ（Starter の書き出し）なら

```
dotnet run --project <ホストのルート>/Source/LowCodeApp.Server --launch-profile https
```

  （Starter リポジトリそのものでは `Source/Hosts/Cookie/LowCodeApp.Server`）。URL に応答が返るまで待つ（起動に 10〜20 秒）。ホストが同居していない（起動方法が分からない）ときだけ URL をユーザーに聞く
- 自分で起動したサーバーは、`*.mod.cs` / スキーマ変更のときは止めて起動し直し、作業の終わりに止める。Claude Code はバックグラウンドタスクの停止で止まる。それ以外のエージェントは起動時にプロセス Id を控え（`$p = Start-Process dotnet -ArgumentList 'run','--project','<ホストのルート>/Source/LowCodeApp.Server','--launch-profile','https' -PassThru; $p.Id`）、終わりに子プロセスごと止める（`Get-CimInstance Win32_Process -Filter "ParentProcessId=<Id>" | ForEach-Object { Stop-Process -Id $_.ProcessId }; Stop-Process -Id <Id>`）。**ユーザーが起動しているサーバーは勝手に止めない**。再起動が必要ならその旨を伝える
- 標準テンプレートから作ったデザインは Cookie 認証付き。初期ユーザーは `admin` / `admin`（テンプレートの SQLite に入っている）

## 2. 自分の編集を反映する（deploy）

デザインプロジェクトへの直接編集は稼働サーバーに自動反映されない。CLI で送る:

```
"<デザイナexeのパス>" deploy "<デザインプロジェクトのフォルダ>" --out "<スクラッチパッド>/deploy.json"
```

- 送れるのは、現在のデプロイ先（`designer.settings.Development.json` の `CurrentDeployInfoName`）の **`AllowCliDeploy` が `true`** のときだけ（方式 FileSystem / FTPS は問わない）。`false` なら拒否メッセージが返る。その場合はデザイナの「デプロイ先の追加」で「CLIからのデプロイを許可する」を付けるか、`AllowCliDeploy: true` の設定をユーザーに依頼する（`Development.json` を自分で書き換えない）
- `template-create --deploy-dir` で作ったデプロイ先は最初から `true`
- 反映後、サイドバー構造を dump して「いま配信されているデザインが自分の編集か」を確かめてから判定に入る

| 変更内容 | 反映に必要なこと |
|---|---|
| `*.mod.json` / `*.frm.json` / `*.sql` / `Resources/`（`app.css` 含む）などのデザインファイル | `deploy` だけ（ホットリロードで反映。サーバー再起動不要） |
| C# スクリプト `*.mod.cs` | `deploy` + **サーバー再起動** |
| テーブル定義（DDL）の変更 | **サーバー再起動**（列定義をキャッシュしている） |

## 3. Playwright のセットアップ（初回のみ）

ブラウザを操作するツールを持つエージェント（VS Code の GitHub Copilot など）は、それで 4 と同じこと（ログイン → 対象ページ → DOM の値とスクショ）をしてよい。そのときはこの節と 4 の雛形は要らない。

`node` / `npm install` / `npx playwright` / `dotnet run` は許可済み（デザイナの AI Workspace が生成する。許可の所在はエージェントごと: ルートの `CLAUDE.md`「Claude Code 固有」/ `AGENTS.md`）。依存はワークスペースの `tools/` に入れる（`.gitignore` 済み）。`cd` せずに、フォルダを引数で渡す:

```
npm install --prefix "<ワークスペース>/tools" playwright
node "<ワークスペース>/tools/node_modules/playwright/cli.js" install chromium
```

スクリプトは `tools/` に置く（`node_modules` を解決するため）か、スクラッチパッド（無ければ OS の一時フォルダ）に置いて `NODE_PATH=<ワークスペース>/tools/node_modules` を付けて実行する。スクショや dump の出力先はスクラッチパッド。

## 4. スクリプトの雛形（ログイン → ページ遷移 → スクショ + DOM）

```javascript
const { chromium } = require('playwright');
const BASE = process.env.SERVER_URL || 'https://localhost:7137';

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({
    viewport: { width: 1400, height: 2500 },
    ignoreHTTPSErrors: true,
  });

  // ログイン (Cookie 認証ホストの login.html。要素 id は Id / Password / LoginButton)
  await page.goto(BASE + '/login.html', { timeout: 60000 });
  await page.fill('#Id', 'admin');
  await page.fill('#Password', 'admin');
  await page.click('#LoginButton');

  // Blazor WASM の初回ロードは 15 秒程度かかる
  await page.waitForURL(url => !url.pathname.startsWith('/login'), { timeout: 60000 });
  await page.waitForTimeout(15000);

  // まず構造を dump してセレクタを確定する (決め打ちは複製・非表示要素で外れる)
  // サイドバーは PC 用とモバイル用の 2 つが描画され、片方は非表示。:visible で見えている方だけを取る
  const links = await page.locator('.sidebar-nav a:visible').allInnerTexts();
  console.log(JSON.stringify(links));

  // 対象ページへ
  await page.locator('.sidebar-nav a:visible', { hasText: '対象ページ名' }).first().click();
  await page.waitForTimeout(3000);

  // DOM から検証値を取る (件数・テキスト・位置)
  const rows = await page.locator('table tbody tr').count();
  const box = await page.locator('.some-selector').first().boundingBox();
  console.log(JSON.stringify({ rows, box }));

  await page.screenshot({ path: process.env.SHOT_PATH || 'output.png' });
  await browser.close();
})();
```

### ポイント

- **WASM の初回ロードは時間がかかる** — ログイン後 `waitForTimeout(15000)` 程度待つ
- **自己署名証明書** — `ignoreHTTPSErrors: true` を必ず付ける
- **viewport の height を大きくする** — 縦に長いページでも `fullPage: true` なしで収まる
- **ページ遷移後も待機を入れる** — クリック後に 2〜3 秒待つと描画が安定する
- **検索初期化スクリプト（`OnSearchInitialization`）を試すときは URL に `?initialize_search=true` を付ける**（サイドバーからの遷移では自動で付く）
- `playwright` 本体と `@playwright/test` は別物。導入したパッケージと使う API を一致させる
- **ボタンに見えても `button` 要素とは限らない**（一覧の「Create」など）。`getByRole('button', { name: ... })` で見つからなければ `getByText('Create', { exact: true })` で取る。迷ったら DOM を dump して確かめる
- **一覧の明細行の入力**は `table tbody tr` の中の `input:visible` で取れる。数量などの入力から計算する値は、入力後に `blur()`（フォーカスを外す）してから読む
- スクリプトのファイルは、作り終えてから実行する（作成と実行を同じ並列の呼び出しに入れると、まだ無いファイルを実行して失敗する）

## 5. 結果の判定

スクショの目視だけで「だいたい合っている」と判断しない。DOM から取ったテキスト・行数・`boundingBox` の位置とサイズを期待値と数値で突き合わせる。ピクセル計測が要るときはスクショを画像ライブラリで測る。判定に使った値は完了報告に添える。

# ユーザーモジュールと認証連動

業務アプリ全体の前提となる「ログインユーザーをアプリ内のレコードとして持つ」「現在のログインユーザーを画面・スクリプトから参照する」「パスワードを変更する」といった基本パターン。

## アプリの作り

<!-- 画像参照: Manual の Image/web/patterns/auth_my_profile.png (ここではコメントアウト) -->

- ユーザーがログインすると、サイドバーに「マイプロフィール」リンクが表示される
- マイプロフィールを開くとログイン中ユーザーの情報 (表示名・メール等) が読み取り専用で表示される
- 「パスワード変更」ボタンでダイアログが開き、その場でパスワードを変更できる
- 管理画面の「ユーザー管理」ではすべてのユーザーを CRUD できる (管理者のみ)

## 支えるデータ構造

```
app_users  (プレーンなユーザーテーブル。ASP.NET Identity ではない)
├── id          PK
├── user_name   TEXT (ログイン ID)
├── name        TEXT (表示名)
├── hash        TEXT (Extras の PasswordHashField がサーバ側で書き込む)
├── salt        TEXT (同上)
├── role        TEXT
└── is_active   BOOLEAN
    (任意) totp_secret TEXT / totp_confirmed INTEGER / totp_last_timestep INTEGER … 認証アプリ (TOTP) の二要素認証を使うときだけ
```

`AppUser` は既定の Cookie 認証が使う**プレーンな `app_users` テーブル**に紐づく (ASP.NET Identity / `AspNetUsers` ではなく、独自ハッシュで照合する素朴な実装)。CLB の `app.clprj` の `CurrentUserModuleDesignName: "AppUser"` で「現在のログインユーザー = AppUser のレコード」と紐づけ、スクリプトから `CurrentUser.表示名.Value` のようにアクセスできるようになる。認証そのものはライブラリではなくホスト (Server プロジェクトの `CookieAuthentication.cs` / `Controllers/AccountController.cs`) の担当で、デザイン側が満たすのは下の「CLB ではこう作る」の契約だけ。ログイン後の `CurrentUser` と権限条件 (認可) はライブラリの担当 → [認可](ClaudeCodeForDesigner/_specs/Authorization.md)。

## モジュールとテーブルの対応

| モジュール | テーブル | 主な役割 |
|---|---|---|
| `AppUser` | `app_users` | ユーザーマスタ。管理画面で CRUD |
| `MyProfile` | (なし、表示専用) | ログイン中ユーザーの自分用情報表示 + パスワード変更ボタン |
| `ChangePasswordDialog` | `app_users` (同じテーブル) | 自分のパスワードだけ更新できるダイアログ用モジュール |

## CLB ではこう作る

- **AppUser モジュール**: 通常の CRUD モジュールとして `app_users` テーブルに紐づける。`Codeer.LowCode.Blazor.Extras` の `PasswordHashField` でハッシュ管理
- **ログインアカウント契約 (`LoginAccountContractField`、Extras)** を AppUser の Fields に 1 つ置く。サーバのログイン処理はこの契約だけを見る:
  `LoginName` (ログイン ID を持つフィールド。必須) / `DisplayName` (表示名) / `IsActive` (偽なら拒否) / `ExternalLoginName` (Entra 等の外部ログインで突き合わせる列。空なら LoginName) と、
  パスワード照合用の `DbColumnPasswordHash` / `DbColumnPasswordSalt` (PasswordHashField と同じ hash / salt 列)。UI もデータも持たない宣言だけのフィールドで、レイアウトには出さない
- 認証アプリ (TOTP) の二要素認証を使うなら、契約の `DbColumnTotpSecret` / `DbColumnTotpConfirmed` / `DbColumnTotpLastTimestep` に上の 3 列を指定する (3 つ揃えて有効化)。
  解除ボタンは `TotpResetButtonField` (AppUser の詳細画面。表示中のユーザーを解除。通常の保存と同じ権限) と `MyTotpResetButtonField` (本人用。どこにでも置ける)
- **app.clprj** の `CurrentUserModuleDesignName: "AppUser"` を指定 → スクリプトの `CurrentUser` から AppUser インスタンスにアクセスできるようになる
- **MyProfile** は表示専用モジュール (`DbTable: ""`)。`CurrentUser.表示名.Value` 等を Label/Text に流し込んで表示
- **パスワード変更**は ChangePasswordDialog (同じ `app_users` テーブルを参照する別モジュール) を `ShowDialog` で開く

## 標準パターン集の対応 (認証・権限)

- サイドバー **`認証・権限/マイプロフィール`** → `MyProfile`
- サイドバー **`認証・権限/ユーザー管理`**、および **`管理画面へ` → `ユーザー管理`** → `AppUser` (管理画面側は管理者のみアクセス)

## 落とし穴

- `AppUser` に `UserReadCondition` / `UserWriteCondition` を**つけてはいけない** (CurrentUser のソースになるため、制限すると マイプロフィール / パスワード変更 / LinkField 表示が全部壊れる)。管理者だけアクセスさせたい場合は PageFrame レベル (`AdminHome.UserReadCondition`) で絞る → [管理画面の分離パターン](auth_admin_frame.md)
- パスワードは平文保存しない。`PasswordHashField` (Extras パッケージ) を使う
- `LoginAccountContractField` が無い、または `LoginName` が空だとログインできない (デザインチェックがエラーにする)。契約の hash / salt 列は PasswordHashField の列と同じにする

## 関連ドキュメント

- [認証・権限・承認パターン 一覧](auth_patterns.md)
- [認証 / 認可の概要](https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Manual/blob/main/JP/authorization/authorization.md)

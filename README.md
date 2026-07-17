# Codeer.LowCode.Blazor.Designer.Standard

**Codeer.LowCode.Blazor** デザイナの標準実装です。プロジェクトテンプレート、ツールメニュー、アイコン候補、AI チャットアシスタントなど、デザイナアプリケーションが通常同梱する機能を一式パッケージ化しています。これにより Visual Studio のテンプレート出力を薄く保ち、機能更新はコードの再生成ではなく NuGet のバージョン更新として届きます。

**MIT ライセンス**のもとオープンソースとして配布しています。

## 機能

- **プロジェクトテンプレート**（`StandardTemplates`）— 空 / 認証付きの空 / 入門 / パターンショーケース / 認証パターン / 在庫管理 / SFA / プロジェクト管理。サンプル SQLite データベースを同梱。
- **ツールメニュー**（`StandardMenus`）— Import Modules from Database、Create DDL、Create Field Class、Create FieldData Class、Create C# Enum、Export Excel Print CheatSheet、Export PageObject（Selenium）、および標準の DB 列変換（PostgreSQL の `xmin` → 楽観的ロック）。
- **アイコン候補**（`StandardIcons`）— アイコンピッカー用の Bootstrap Icons 一覧。
- **Claude Code ワークスペース**（`ClaudeWorkspaceDeploy` / Tools > Claude Code Workspace）— Claude Code ワークスペース（`CLAUDE.md`・ドキュメント・フック・デザイナ exe パスを焼き込んだ許可設定）を展開・更新します。ドキュメントはこのパッケージ内（`ClaudeWorkspace/`）の同梱分に加えて、ai-refresh による自動生成リファレンス（フィールドカタログ・仕様・デフォルト JSON・サンプル）を `ClaudeCodeForDesigner/` に丸ごと出力し、デザイナ更新時はフックが検知して丸ごと入れ替えるため、常に実行中のデザイナと同一バージョンになります。headless CLI の `claude-workspace` verb からも利用できます。使い方は [Claude Code でデザインプロジェクトを編集する](Docs/claude_code_designer.md) を参照してください。
- **DDL 生成**（`DbMapping`）— モジュール定義からの決定的な CREATE TABLE / 差分 ALTER TABLE 生成。
- **AI チャットアシスタント**（`AIChat/`）— CSS・スクリプト・SQL・モジュール設定・フィールド・レイアウト（詳細 / 一覧 / 検索）・ページフレームを自然言語で編集。
  - *タスク指向*: インテントルーターが要求を手順に分解し、各手順を適切なエディタ機能へ振り分けます。
  - *自己修正*: 生成された変更はデザイナのデザインチェックを通し、エラーがあれば適用前に再生成します。
  - *モデル差し替え可能*: [`Microsoft.Extensions.AI`](https://learn.microsoft.com/dotnet/ai/) の `IChatClient` 抽象の上に構築。任意のプロバイダを注入できます。
  - *カスタムフィールド対応*: デザイナの `FieldCatalog` に登録されたカスタムフィールド型を認識します。
  - *プロンプト編集可能*: 仕様はデザイナの `SpecDocCatalog` から、ガイドラインは同梱の Claude Code ワークスペースのドキュメントから読み込みます。フォークして自前の内容でリビルドできます。

## インストール

```
dotnet add package Codeer.LowCode.Blazor.Designer.Standard
```

デザイナパッケージ（`Codeer.LowCode.Blazor.Designer`、同一バージョン）が必要です。

## はじめに

デザイナアプリで 2 回の呼び出しを行い、すべてを登録します。`SetupHeadless` は `base.OnStartup(e)` の**前**に（プロジェクトテンプレートと CLI verb は `base.OnStartup` 内で動く headless CLI が使うため）、`Setup` はその後に呼びます。

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    // ... アプリ固有のサービス / スクリプト型の登録 ...
    DesignerStandard.SetupHeadless(); // テンプレート + claude-workspace CLI verb

    base.OnStartup(e);

    DesignerStandard.Setup(DesignerEnvironment, new DesignerStandardOptions
    {
        // null にすると AI チャットは無効。プロバイダの選択と資格情報はアプリ側に置く。
        CreateAiChatClient = () => new AzureOpenAIClient(endpoint, credential)
            .GetChatClient(model).AsIChatClient(),
    });
}
```

あるいは機能を個別に選ぶこともできます（`Setup` は公開された構成要素の総和にすぎません）。

```csharp
StandardIcons.AddBootstrapIcons();
DesignerTemplateCandidate.Templates.Add(StandardTemplates.PatternShowcase()); // 一部だけ
StandardMenus.AddCreateDdl(DesignerEnvironment);                              // メニューも個別に
DesignerChatRegistration.RegisterScreenChats(
    new DesignerEnvironmentChatHost(DesignerEnvironment), createAiChatClient);
```

## AI チャットのアーキテクチャ

```
Screen chats (ScreenChats)          エディタごとの入口
        |
   AiOrchestrator + IntentRouter    要求を順序付きの手順に分解する
        |
   Functions/ (IAiFunction)         field.create / field.edit / layout.* /
        |                           script.edit / sql.* / css.edit / db.update / ...
   IDesignerChatHost                デザイナへのブリッジ
                                    (DesignerEnvironmentChatHost が標準実装)
```

プロンプトは単一ソースです。フレームワークの仕様はデザイナパッケージ（`SpecDocCatalog`、実装と同一アセンブリ・同一バージョン）から、作成ガイドラインは同梱の Claude Code ワークスペース（`ClaudeWorkspace/ClaudeCodeForDesigner/Docs/`）から取得します — AI チャットと Claude Code は同じドキュメントを読みます。
フィールドとスクリプトオブジェクトの知識は、デザイナの `FieldCatalog` / `ScriptObjectCatalog`（拡張ライブラリが登録したドキュメントを含む）から動的に注入されるため、プロンプトを編集しなくても拡張が反映されます。

## ライセンス

MIT ライセンス。[LICENSE](LICENSE) を参照してください。

# Codeer.LowCode.Blazor デザインワークスペース

このフォルダで Codex / GitHub Copilot を使い、Codeer.LowCode.Blazor のデザインファイル（JSON / C# スクリプト / SQL / CSS）を編集する。

## まず CLAUDE.md を読む

**規約の本体は同じフォルダの `CLAUDE.md`。着手前に全文を読み、それに従う**（名前は Claude だが、どのエージェントにも共通の規約。
`AIForDesigner/` の文書も同じ）。末尾の「Claude Code 固有」の節だけは読み飛ばし、代わりにこのファイルに従う。
このフォルダの `CLAUDE.md` が 1 行目が `# Codeer.LowCode.Blazor デザインワークスペース` でない別のもの（ホストソリューションの説明）なら、
規約の本体は `AIForDesigner/WorkspaceRules.md`（中身は同じ）。

## Codex

- 許可とフックはデザイナ（Tools > AI Workspace / `ai-workspace` CLI）が、このマシンの exe パス入りで生成する。手で編集しない（exe のパスが変わったときは `CLAUDE.md` の「exe に繋がらないとき」）
  - `.codex/hooks.json` — セッション開始と各プロンプトで `AIForDesigner/` を最新化し、ライセンスを確かめる。ファイル編集の前に、デザインを編集できないライセンスなら止める
  - `.codex/rules/codeer-lowcode.rules` — デザイナ exe の CLI・`dotnet build` / `test` / `run`・`node`・`npm install`・`npx playwright` を確認なしで実行する
- この 2 つは **Codex がこのフォルダ（プロジェクト）を信頼済みにしたときだけ読まれる**。フックはさらに `/hooks` で中身を確認して信頼するまで動かない（exe のパスが変わって中身が変わったときも確認し直す）。`AIForDesigner/` が無い・CLI のたびに確認が出るなら、この 2 点をユーザーに案内する
- フックが動いていないときは、着手前に自分で最新化する（exe のパスは `LocalEnvironment.md` の `DesignerExePath:`、デザインプロジェクトのフォルダは既定 `design`）:
  `powershell -NoProfile -ExecutionPolicy Bypass -File ".claude/refresh-ai-workspace.ps1" "<デザイナexeのパス>" "<デザインプロジェクトのフォルダ>"`
  出力に `[ライセンス]` があれば、`CLAUDE.md` の「ライセンス」に従う
- ファイルの作成・編集は apply_patch、読み込み・検索はシェル（`rg` 等）で行う
- デザイナ exe の CLI は PowerShell から `Start-Process -Wait` で呼ぶ（`CLAUDE.md`「実行方法」の PowerShell の注意。`& "<exe>" ...` では終了を待てない）。この呼び方はルールの前方一致に当たらないことがあり、そのときは承認が出る

## GitHub Copilot

- フックは Claude Code と同じ `.claude/settings.local.json` のものを Copilot（VS Code のエージェント・Copilot CLI）が読んで動かす（最新化とライセンスの判定）
- **フックの出力は Copilot には届かない**（最新化の結果・`[ライセンス]` の知らせ）。着手前に自分で最新化とライセンスの確認を実行する（exe のパスは `LocalEnvironment.md` の `DesignerExePath:`、デザインプロジェクトのフォルダは既定 `design`）:
  `powershell -NoProfile -ExecutionPolicy Bypass -File ".claude/refresh-ai-workspace.ps1" "<デザイナexeのパス>" "<デザインプロジェクトのフォルダ>"`
  出力に `[ライセンス]` があれば、`CLAUDE.md` の「ライセンス」に従う（デザインは編集しない）。ファイル編集を止めるフック自体は Copilot でも動く
- コマンドの許可は Copilot の承認で行う（このワークスペースは Copilot の許可設定を生成しない）。デザイナ exe の CLI を毎回確認したくないなら、ユーザーに Copilot の自動承認の設定（VS Code なら `chat.tools.terminal.autoApprove`）へ足してもらう
- デザイナ exe の CLI は PowerShell から `Start-Process -Wait` で呼ぶ（`CLAUDE.md`「実行方法」の PowerShell の注意）

## 共通の注意

- `designer.settings.Development.json`（接続文字列・デプロイ設定）は、Claude Code と違って読み書きが仕組みでは止まらない。`CLAUDE.md` の規則どおり、ユーザーが明示的に依頼しない限り読まない・書かない
- 許可やフックの設定を書き換えたら、次のセッションから効く。新しいセッションを始めるよう案内する

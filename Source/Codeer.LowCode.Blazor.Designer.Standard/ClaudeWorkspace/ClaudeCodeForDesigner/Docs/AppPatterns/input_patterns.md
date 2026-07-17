# 入力 UX のパターン

フォームの入力体験を向上させる各種パターン。検証・連動・自動計算・既定値・キー操作などをスクリプトと宣言の組み合わせで実現します。

---

## 入力検証

<!-- 画像参照: Manual の Image/web/patterns/input_validation.png (ここではコメントアウト) -->

フィールドの `IsRequired: true` で必須入力、`OnValidateInput` スクリプトで独自検証を追加。検証 NG なら `SetError(message)` を呼んで赤枠 + エラーメッセージを表示。Submit 時は CLB が自動で全フィールドの検証を走らせる。

**標準パターン集の対応**: サイドバー **`入力UX/入力検証 → `ValidationSample``**

---
## 固定候補の共有 (デザイン enum)

「受注/出荷済/完了」のような固定候補が**複数モジュールで使われる、またはスクリプトが候補値で分岐する**場合は、各 SelectField の `Candidates` 直書きではなく、プロジェクト共有の **enum 定義** (`Enums/{名前}.enum.json`) を作って `SelectFieldDesign.EnumName` で参照する。候補の変更が 1 箇所で済み、スクリプトから `Status.Value = OrderStatus.Received;` のように保存値を型安全に参照できる (enum に無い値の代入・比較は designcheck が検出)。その画面限りの候補なら `Candidates` 直書きでよい。ファイル形式・省略ルール・リネーム (`rename-enum` / `rename-enum-member`) は [ClaudeCodeForDesigner/_specs/DesignEnums.md](ClaudeCodeForDesigner/_specs/DesignEnums.md) を参照。

---
## 連動入力 (カスケーディング選択)

<!-- 画像参照: Manual の Image/web/patterns/input_cascade.png (ここではコメントアウト) -->

`SelectField` / `RadioGroupField` の選択値に応じて、別フィールドの候補が動的に切り替わるパターン。スクリプトで親の `OnDataChanged` を契機に子の Candidates を組み立てる。

**標準パターン集の対応**: サイドバー **`入力UX/連動入力 → `CascadeInput``**

---
## 連動入力 (検索条件で宣言)

<!-- 画像参照: Manual の Image/web/patterns/input_cascade_search.png (ここではコメントアウト) -->

`SelectField.SearchCondition` に `FieldVariableMatchCondition` で「親フィールドの値で絞り込む」を**宣言的に**書ける (スクリプト不要)。マスタテーブルから動的に候補を引くケースに最適。

**標準パターン集の対応**: サイドバー **`入力UX/連動入力 (検索条件) → `CascadeInputBySearch``**

---
## 自動計算 (依存フィールド)

<!-- 画像参照: Manual の Image/web/patterns/input_calc.png (ここではコメントアウト) -->

あるフィールドの `OnDataChanged` スクリプトで別フィールドの値を計算更新するパターン。数量 × 単価 = 小計、開始日 + 期間 = 終了日 などの典型ケース。

**標準パターン集の対応**: サイドバー **`入力UX/計算 → `CalcSample``**

---
## 既定値の自動セット

<!-- 画像参照: Manual の Image/web/patterns/input_default.png (ここではコメントアウト) -->

新規作成時に `OnAfterInitialization` で `Field.Value = DateTime.Now` / `CurrentUser.Id.Value` などを入れる。または `DefaultValueAttribute` 系で宣言的に。CLB の予約名 `Creator` / `CreatedAt` は CLB 自動セットなので別途代入不要。

**標準パターン集の対応**: サイドバー **`入力UX/既定値 → `DefaultValueSample``**

---
## キーボードショートカット

<!-- 画像参照: Manual の Image/web/patterns/input_shortcut.png (ここではコメントアウト) -->

`GridLayout.OnKeyDown` スクリプトでキー入力 (`Alt+S` 保存、`Ctrl+Enter` 送信、`Esc` クリア等) を受け取って、対応するアクションを実行。マウス操作なしで業務処理を完結させたい現場向け。

**標準パターン集の対応**: サイドバー **`入力UX/ショートカット → `ShortcutSample``**

---
## ファイル添付

<!-- 画像参照: Manual の Image/web/patterns/input_attach.png (ここではコメントアウト) -->

`FileField` でレコードに任意のファイルを添付。サーバー側 `appsettings.json` の `TemporaryFileTableInfo` + 一時ファイルテーブル + `FileStorages` の 3 点セット設定が必要 (JSON だけでは動かない)。

**標準パターン集の対応**: サイドバー **`入力UX/添付 → `AttachmentSample``**

---
## 画像アップロード + プレビュー

<!-- 画像参照: Manual の Image/web/patterns/input_image.png (ここではコメントアウト) -->

`ImageField` (= 画像専用 FileField) で画像のアップロードと**サムネプレビュー表示**を組み合わせる。商品画像・プロフィール写真などに。

**標準パターン集の対応**: サイドバー **`入力UX/画像 → `ImageUploadSample``**

---

## 関連ドキュメント

- [アプリ作成パターン一覧](patterns.md) ─ 全パターンのインデックス
- [モジュール定義の全体構造](ClaudeCodeForDesigner/_specs/ModuleDesign.md)
- [Field リファレンス](../Fields/)

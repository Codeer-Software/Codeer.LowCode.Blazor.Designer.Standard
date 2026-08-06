# 取込書出 (CSV / Excel の一括入出力)

**いつ使う**: 既存データを CSV/Excel でダウンロードして、編集して、まとめてアップロードで反映する。大量データの初期投入や、業務担当が表計算ソフトで編集したい場合の定番。

## アプリの作り

<!-- 画像参照: Manual の Image/web/patterns/import_export.png (ここではコメントアウト) -->

- 一覧画面上部に **ダウンロード / アップロード** ボタンが表示される
- ダウンロードボタンで一覧の内容を Excel ファイルとして取得
- Excel を編集してアップロードすると、内容が DB に反映される (新規追加 / 既存更新)

## 支えるデータ構造

```
import_exports
├── id           PK
├── name         TEXT
├── amount       NUMBER
└── record_date  DATE
```

通常の CRUD テーブル。アップロード/ダウンロードは CLB の標準機能で自動処理される。

## モジュールとテーブルの対応

| モジュール | テーブル | 主な設定 |
|---|---|---|
| `ImportExport` | `ImportExports` | PageFrame の Link 側で `CanBulkDataUpdate: true` (アップロード) + `CanBulkDataDownload: true` (ダウンロード) を有効化 |

## CLB ではこう作る

- 通常の CRUD モジュールとして定義
- **PageFrame の Link** の `ListPageDesign` で `CanBulkDataUpdate` / `CanBulkDataDownload` を `true` にすると、一覧ページにアイコンボタンが出る
- Excel フォーマットはモジュールの ListLayout に基づいて自動生成される

## 標準パターン集の対応

サイドバー **`データ操作/取込書出`** → `ImportExport`

## 落とし穴

- `CanBulkDataUpdate` / `CanBulkDataDownload` は **PageFrame の Link 側で設定する**。モジュール側の同名プロパティだけでは一覧画面のアイコンが出ない
- ビルトイン機能の既定は Excel 形式。CSV / 固定長は、モジュールにファイル形式定義のフィールド (`_field_catalog.md` の一括入出力系フィールド) を置くと対応できる

## スクリプトで加工しながら取り込む場合

ボタン一発の取込で足りない場合 (コード変換・検証・マスタ引き当て等を挟みたい場合) は、
スクリプトの一括入出力オブジェクト (ファイル取込 → 行の加工 → 一括保存) を使う。`_script_catalog.md` の該当オブジェクトを参照。
大量行の加工は `ModuleData` のまま行うのが速い (→ [スクリプト作成ガイドライン](../ScriptGuidelines.md) の「大量行は ModuleData（Raw 系）で扱う」)。

## 関連ドキュメント

- [アプリ作成パターン一覧](patterns.md) ─ 全パターンのインデックス
- [モジュール定義の全体構造](ClaudeCodeForDesigner/_specs/ModuleDesign.md)
- [PageFrame の設定](ClaudeCodeForDesigner/_specs/PageFrame.md)

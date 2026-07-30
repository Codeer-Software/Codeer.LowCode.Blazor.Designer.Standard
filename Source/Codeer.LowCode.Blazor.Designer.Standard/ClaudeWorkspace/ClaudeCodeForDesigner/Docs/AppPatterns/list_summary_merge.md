# グループ集計表 (サマリー行 + セル結合)

ListField をスクリプトで装飾して、Excel 帳票風の「グループ化された集計表」を作るパターン。
デザイン (JSON) 側に設定項目は無く、**すべてスクリプト (*.mod.cs) から設定する**。

| 機能 | API (すべて ListField) | 効果 |
|---|---|---|
| 合計行 | `AddSummaryRow()` | 表の下端に張り付く表示専用の行 (`ListSummaryRow`) を追加 |
| 小計行 | `InsertSummaryRow(int 行index)` | データ行の直後に表示専用の行を挿入 |
| 縦セル結合 | `MergeRows(列名, 開始行, 行数)` / `MergeSameRows(列名)` | 列内の連続セルを 1 セルに結合 (値は先頭行・縦中央) |
| ヘッダー横結合 | `MergeHeaderColumns(列名, 列数[, 見出しテキスト])` | 列ヘッダーのセルを横に結合してグループ見出しに |
| ヘッダー見出しの上書き | `SetHeaderText(列名, テキスト)` | ヘッダーの表示テキストをデザインのラベルより優先して差し替え (「数量(kg)」等の動的見出し。null で解除) |
| 解除 | `ClearSummaryRows()` / `ClearRowMerges()` / `ClearHeaderMerges()` / `ClearHeaderTexts()` | それぞれ全解除 |

---

## いつ使う

- 売上一覧をカテゴリでまとめ、カテゴリ名は縦に 1 セルで見せたい (同値の繰り返しを消す)
- グループの区切りに小計、末尾に総合計を出したい
- 「単価・数量・金額」の 3 列の上に「金額情報」のようなグループ見出しを付けたい

## アプリの作り

一覧はグループ列でソートしておく (`SearchCondition.SortConditions`)。同値が隣接して並ぶことで
縦結合が「グループのまとまり」に見える。集計値 (小計・合計) の計算はスクリプトの責務で、
製品は「列に揃った表示行」と「セル結合」だけを提供する。

## CLB ではこう作る

モジュール定義 (mod.json) 側は通常の ListField + ListLayout のみ。スクリプトで組み立てる。

```csharp
// mod.json: Items の { "OnDataChanged": "RebuildView" }、DetailLayout の { "OnAfterInitialization": "InitView" }
void InitView()
{
    //ヘッダー構造は静的なので一度だけ。テキスト省略時は起点列のラベルを表示
    Items.MergeHeaderColumns("Category", 2, "分類");  //Category〜SubCategory の 2 列を「分類」1 セルに
    Items.MergeHeaderColumns("UnitPrice", 3, "金額情報");
    RebuildView();
}

void RebuildView()
{
    //並びが変わるたびに全部組み直す (位置指定は自動追従しない)
    Items.ClearSummaryRows();
    Items.ClearRowMerges();

    var rows = Items.Rows;
    var i = 0;
    decimal sub = 0;
    decimal total = 0;
    foreach (var row in rows)
    {
        if (row.Amount.Value != null)
        {
            sub = sub + row.Amount.Value;
            total = total + row.Amount.Value;
        }
        //グループ末尾 (次の行が別カテゴリ or 最終行) の直後に小計
        var isGroupEnd = i == rows.Count - 1 || rows[i + 1].Category.Value != row.Category.Value;
        if (isGroupEnd)
        {
            var s = Items.InsertSummaryRow(i);
            s.MergeColumns("Category", 2);  //サマリー行のセルも横結合できる
            s.SetText("Category", row.Category.Value + " 小計");
            s.SetText("Amount", sub.ToString("#,0"));
            s.BackgroundColor = "#E3F2FD";
            sub = 0;
        }
        i = i + 1;
    }

    var sum = Items.AddSummaryRow();
    sum.SetText("Category", "合計");
    sum.SetText("Amount", total.ToString("#,0"));
    sum.BackgroundColor = "#FFF8E1";
    sum.SetColor("Amount", "#D32F2F");

    //縦結合: 隣接する同値セルをまとめる。小計行の位置では自動で分断されるので順序を意識しなくてよい
    Items.MergeSameRows("Category");
    Items.MergeSameRows("SubCategory");
}
```

- 縦結合したい列は ListLayout の列で `IsViewOnly: true` にする (編集できる列は結合されない)
- 列固定 (`FixedColumnCount`) と併用できる。固定対象列には幅 (Width) 指定が必要
- 任意の範囲を明示的に結合したいときは `MergeRows("Category", 0, 4)` (開始行, 行数)。範囲の重複は先に指定した方が勝つ

## 落とし穴

- **どの機能も条件を満たさないと黙って通常表示に倒れる** (エラーは出ない)。効かないときは①ListLayout が 1 行構成 (結合なし) か ②縦結合の対象列が ViewOnly か ③横結合が列固定の境界を跨いでいないか、を確認する
- **ソート・ページング・検索・行の増減に自動追従しない**。`OnDataChanged` (検索条件系は `OnSearchDataChanged`) で `ClearRowMerges()` / `ClearSummaryRows()` から組み直す
- `MergeSameRows` の同値判定は値の等価比較。**スクリプトの `/` は整数同士でも decimal 除算**なので、連番からグループ番号を計算するときは `Math.Floor` を使う (`(i - 1) / 4 + 1` は 1.25 のような端数になり全行が別値になる)
- 結合したヘッダーはソート・列幅リサイズ・列カスタマイズのドラッグ対象にならない。ユーザーソートさせたい列はヘッダー結合の範囲に入れない
- サマリー行は表示専用で保存・送信データには入らない。集計を DB に問い合わせ直すのではなく画面上の `Rows` を集計する ([スクリプト作成ガイドライン](../ScriptGuidelines.md)参照)

## 関連ドキュメント

- [リスト系フィールドの使い分け](list_patterns.md)
- ListField の全 API: フィールド型カタログ (`ClaudeCodeForDesigner/temporary/_field_catalog.md`) の ListField 節

# JsonAbstract と TypeFullName ルール

## 基本ルール

`JsonAbstract` を継承するクラスのオブジェクトは、**JSON上で必ず `TypeFullName` を含める必要がある**。
これは `JsonAbstractClassConverter` がデシリアライズ時に具象型を判別するために使用する。

```csharp
// JsonAbstractClassConverter の処理
if (!document.RootElement.TryGetProperty("TypeFullName", out JsonElement typeProperty) &&
    !document.RootElement.TryGetProperty("typeFullName", out typeProperty))
    throw new JsonException("TypeFullName property is missing.");
```

`TypeFullName` が1つでも欠けると、**モジュール全体の読み込みが失敗する**。

---

## デザインファイルで出現する JsonAbstract 継承クラス

デザインファイル（`*.mod.json`）を手書きする際に TypeFullName の指定が必要な箇所は以下の通り。

### 1. Fields 配列の各フィールド（FieldDesignBase 継承）

**出現場所:** `ModuleDesign.Fields[]`

すべてのフィールドに TypeFullName が必須。これは通常忘れにくい。

```json
{
  "Name": "ProductName",
  "DbColumn": "product_name",
  "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign"
}
```

**各フィールド型の TypeFullName は、別途動的に渡される「フィールド型カタログ」に（組み込み・外部ライブラリ・独自を含め）列挙されている。それを参照して設定すること。**

### 2. レイアウト（LayoutDesignBase 継承）

**出現場所:** `DetailLayouts.Layout`, `SearchLayouts.Layout`, ネストされたグリッド内

```json
{
  "Rows": [...],
  "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.GridLayoutDesign"
}
```

| レイアウト型 | TypeFullName |
|---|---|
| GridLayoutDesign | `Codeer.LowCode.Blazor.Repository.Design.GridLayoutDesign` |
| SearchGridLayoutDesign | `Codeer.LowCode.Blazor.Repository.Design.SearchGridLayoutDesign` |
| FieldLayoutDesign | `Codeer.LowCode.Blazor.Repository.Design.FieldLayoutDesign` |
| CanvasLayoutDesign | `Codeer.LowCode.Blazor.Repository.Design.CanvasLayoutDesign` |
| TabLayoutDesign | `Codeer.LowCode.Blazor.Repository.Design.TabLayoutDesign` |

**注意:** カラム内にネストする GridLayoutDesign にも TypeFullName が必要。

### 3. 検索条件（MatchConditionBase 継承）⚠️ 間違えやすい

**出現場所:** `SearchCondition.Condition`, `MultiMatchCondition.Children[]`

```json
{
  "IsOrMatch": false,
  "IsNot": false,
  "Children": [...],
  "Name": "",
  "TypeFullName": "Codeer.LowCode.Blazor.Repository.Match.MultiMatchCondition"
}
```

| 条件型 | TypeFullName |
|---|---|
| MultiMatchCondition | `Codeer.LowCode.Blazor.Repository.Match.MultiMatchCondition` |
| FieldValueMatchCondition | `Codeer.LowCode.Blazor.Repository.Match.FieldValueMatchCondition` |
| FieldValueMatchConditionNonNull | `Codeer.LowCode.Blazor.Repository.Match.FieldValueMatchConditionNonNull` |
| FieldVariableMatchCondition | `Codeer.LowCode.Blazor.Repository.Match.FieldVariableMatchCondition` |

### 4. 値オブジェクト（MultiTypeValue 継承）⚠️ 最も間違えやすい

**出現場所:** `FieldValueMatchCondition.Value`

`Value` プロパティの型は `MultiTypeValue`（抽象クラス）のため、TypeFullName が必須。
**ドキュメントの JSON 例には TypeFullName が省略されていることがあるので特に注意。**

```json
// ✅ 正しい
"Value": {
  "Value": "AH",
  "TypeFullName": "Codeer.LowCode.Blazor.Repository.StringValue"
}

// ❌ 間違い（TypeFullName がない）
"Value": {
  "Value": "AH"
}
```

| 値の型 | TypeFullName | 用途 |
|---|---|---|
| StringValue | `Codeer.LowCode.Blazor.Repository.StringValue` | 文字列（Select値, テキスト等） |
| DecimalValue | `Codeer.LowCode.Blazor.Repository.DecimalValue` | 数値 |
| BooleanValue | `Codeer.LowCode.Blazor.Repository.BooleanValue` | 真偽値 |
| DateOnlyValue | `Codeer.LowCode.Blazor.Repository.DateOnlyValue` | 日付 |
| TimeOnlyValue | `Codeer.LowCode.Blazor.Repository.TimeOnlyValue` | 時刻 |
| DateTimeValue | `Codeer.LowCode.Blazor.Repository.DateTimeValue` | 日時 |
| NullValue | `Codeer.LowCode.Blazor.Repository.NullValue` | null |
| BinaryValue | `Codeer.LowCode.Blazor.Repository.BinaryValue` | バイナリ |

---

## チェックリスト

デザインファイルを作成・編集する際は以下を確認すること：

- [ ] `Fields[]` の各フィールドに `TypeFullName` があるか
- [ ] `DetailLayouts` / `SearchLayouts` の `Layout` に `TypeFullName` があるか
- [ ] カラム内にネストした `GridLayoutDesign` に `TypeFullName` があるか
- [ ] `FieldLayoutDesign`（カラムの Layout）に `TypeFullName` があるか
- [ ] `SearchCondition.Condition`（MultiMatchCondition）に `TypeFullName` があるか
- [ ] `Condition.Children[]` の各条件に `TypeFullName` があるか
- [ ] **`FieldValueMatchCondition.Value` に `TypeFullName` があるか** ← 最重要

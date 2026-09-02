# 承認フローのワークフロー

「**申請を書く → 上長や経理が承認する → 申請者に結果が返る**」という、業務アプリで非常によく登場するワークフロー。
CLB では Extras の **`ApprovalFlowField`** で作る。申請書モジュールにフィールドを 1 つ置くと、申請・承認・却下・差し戻し・取り下げ・
再申請・回覧確認と、ステッパー形式の進捗表示・コメント・履歴表示が付く。状態遷移はすべてサーバー (`/api/approval`) が検証する。
自前で承認テーブルや状態遷移スクリプトを書かない (旧「承認フローテンプレート」方式のサンプルは廃止した)。

## アプリの作り (標準パターン集の「承認」グループ)

- 一般ユーザー (alice) が「経費精算」または「休暇申請」を作成して保存し、「申請」を押す
- 経路は申請時にスクリプト (`OnBuildRoute`) が返す。サンプルは承認経路マスタの「経費ルート」(上長承認 = bob → 経理確認 = carol) /「休暇ルート」(上長承認 = bob) を読む
- 承認者 (bob) のサイドバー「承認待ち」に該当申請が並ぶ (`MyApprovalList`。全申請種別を横断)。「開く」で申請書へ遷移し、標準 UI の「承認」「却下」「差し戻し」を押す
- 全ステップ承認で状態 Completed。却下 → Rejected、差し戻し → Returned。申請者は内容を直して「再申請」できる (履歴は積算・試行番号で世代管理)
- 「承認状況」(`ApprovalStatusList`) で申請全体の状態 / 申請者 / 現在の担当者を一覧できる
- 承認経路マスタ (経路 → ステップ → ステップ承認者) は管理画面 (AdminFrame) から編集する

## 支えるモジュール

承認データは通常のモジュール 3 つ (フロー / メンバー / 履歴) に保存され、各モジュールの**契約フィールド**が「役割 → フィールド名」を宣言する。
申請書は FK 列 1 本 (`approval_id`) でフロー行を指し、状態や申請者は**リンク越し参照** (`Approval.Status.Value` 等) で読む。

| モジュール | テーブル | 役割 |
|---|---|---|
| `ExpenseRequest` / `LeaveRequest` | `expense_request` / `leave_request` | 申請書。`ApprovalFlowFieldDesign` (`Approval`, DbColumn `approval_id`) を 1 つ持つ |
| `ApprovalFlow` | `approval_flows` | フロー本体 (状態 / 対象モジュール名・Id / 申請者 / 試行番号 / 現在ステップ / Members・Histories 一覧 / 楽観ロック)。UI なし |
| `ApprovalFlowMember` | `approval_flow_members` | ステップごとの承認者 (必須/任意・完了条件・状態)。UI なし |
| `ApprovalHistory` | `approval_histories` | 操作履歴。UI なし |
| `MyApprovalList` / `ApprovalStatusList` | (QueryField) | 承認待ち / 承認状況の一覧。予約パラメータ `current_user_id` で自分の Waiting 行に絞る |
| `ApprovalRoute` / `ApprovalRouteStep` / `ApprovalRouteStepMember` | `approval_routes` / `approval_route_steps` / `approval_route_step_members` | 経路マスタ (契約なしのただのモジュール。`ApprovalRoute.mod.cs` の `Load(routeName)` が経路を組み立てる) |
| enum `ApprovalTargetModule` | ─ | 一覧の「申請種別」列でモジュール名を表示名に読み替える (メンバー名 = 申請書モジュール名) |

## CLB ではこう作る

### 1. 承認モジュール群を生成する (手で作らない)

デザイナ Tools > 承認フローのセットアップ、または headless CLI:

```
<designer.exe> approval-setup "<デザインプロジェクト>" --data-source <データソース名> --user-module AppUser --user-name-field 表示名 --no-mail --ddl-out ddl.sql
```

フロー / メンバー / 履歴 + 承認待ち・承認状況 + 経路マスタ 3 つ + enum + PageFrame リンク + テーブル作成 DDL ができる。DDL は `sql` CLI で流す。
冪等なので申請書が増えても再実行しない (承認モジュール群は 1 セットを全申請書で共有する)。

### 2. 申請書側 (4 手順)

1. 申請書モジュールに `ApprovalFlowFieldDesign` を置く (`FlowModuleName: "ApprovalFlow"`、`DbColumn: "approval_id"` を DB に追加、`OnBuildRoute: "OnBuildRoute"`)
2. スクリプトに経路組み立てを書く:
   ```csharp
   ApprovalRouteData OnBuildRoute()
   {
       return new ApprovalRoute().Load("経費ルート");   // null を返すと申請中止。金額で経路名を選ぶ等はここで
   }
   ```
3. 編集ロック: 申請書の `DataWriteCondition` に「`Approval.Status.Value` が null (未申請) / Returned / Withdrawn / Rejected」の Or 条件 (条件エディタの行モデル: 値ありは `FieldValueMatchConditionNonNull`、null だけ `FieldValueMatchCondition` + `NullValue`)。詳細レイアウトの `DataOnlyFields` に `Approval.Status` / `Approval.Applicant` / `Approval.Members` を登録
4. enum `ApprovalTargetModule` にメンバーを追加 (名前 = 申請書モジュール名 / 表示 = 申請書の名前)

一覧に状態列を出すなら `ListLayouts` の要素に `Approval.Status` と書く (フロー側 Select の enum 表示がそのまま出る)。

### 3. 権限

- 承認モジュール 3 つの `UserWriteCondition` は誰も満たさない条件 (セットアップが設定する)。承認データはサーバーの内部経路だけが書く
- アプリの Current User Module (`app.clprj` の `CurrentUserModuleDesignName`) が必須
- 承認者だけが書けるフィールド (査定額など) は `PermissionField` に「現在の承認待ち」(`Approval.Status == InProgress` かつ `Approval.Members.Status == Waiting` かつ `Approval.Members.ApproverUser == CurrentUser.Id.Value`) を書く

## 標準パターン集の対応

- サイドバー **`承認/経費精算`** → `ExpenseRequest` (経路: 経費ルート)
- サイドバー **`承認/休暇申請`** → `LeaveRequest` (経路: 休暇ルート)
- サイドバー **`承認/承認待ち`** → `MyApprovalList`、**`承認/承認状況`** → `ApprovalStatusList`
- 管理画面 (`AdminFrame`) **`承認経路マスタ`** → `ApprovalRoute`

## 落とし穴

- フィールドの正確なプロパティ・契約・条件の書き方は `_field_catalog.md` の ApprovalFlowField と `_specs/` を正とする (本書は入口)
- 経路の承認者に申請者自身が含まれると申請できない (サンプルの `ApprovalRoute.Load` がエラーにする)。デモでは alice で申請し、bob / carol で承認する
- 組み込みボタンのアクション後は承認フィールドだけ再読込される。編集ロックのクライアント表示は開き直しで反映 (サーバー強制は即時)
- `ApprovalFlow` 等はエンジン用モジュールで UI を持たない。一覧を作りたいときは QueryField の検索用モジュール (`MyApprovalList` の形) を足す

## 関連ドキュメント

- [認証・権限・承認パターン 一覧](auth_patterns.md)
- [ユーザーモジュールと認証連動](auth_user_module.md) ─ 承認者・申請者の判定に使う CurrentUser
- [検索条件の初期化](search_patterns.md#検索条件の初期化)

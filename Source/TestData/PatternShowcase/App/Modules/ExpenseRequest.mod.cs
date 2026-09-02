// 承認フローのサンプル (経費精算)。
// 申請・承認・却下・差し戻し・取り下げ・再申請のボタンと進捗/履歴表示は ApprovalFlowField (Approval) の標準 UI。
// 承認モジュール群 (ApprovalFlow / ApprovalFlowMember / ApprovalHistory) と承認経路マスタは
// Tools > 承認フローのセットアップ (CLI: approval-setup) が生成したもの。アプリの責務は「経路を返す」ことだけ。
// マスタの読み込みと検証 (経路が無い / 申請者自身が承認者) は経路マスタモジュール (ApprovalRoute.mod.cs) の Load に共通化してある。

// 経路を組み立てる (Approval フィールドの「経路組み立て」に設定。null を返すと申請は中止される)
ApprovalRouteData OnBuildRoute()
{
    return new ApprovalRoute().Load("経費ルート");
}

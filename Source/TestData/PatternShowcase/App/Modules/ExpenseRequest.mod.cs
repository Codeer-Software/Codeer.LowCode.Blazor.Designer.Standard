// 承認フローのサンプル (経費精算)。
// 申請・承認・却下・差し戻し・取り下げ・再申請のボタンと進捗/履歴表示は ApprovalFlowField (Approval) の標準 UI。
// 承認モジュール群 (ApprovalFlow / ApprovalFlowMember / ApprovalHistory) と承認経路マスタは
// Tools > 承認フローのセットアップ (CLI: approval-setup) が生成したもの。アプリの責務は「経路を返す」ことだけ。
// マスタの読み込みと検証 (経路が無い / 申請者自身が承認者) は経路マスタモジュール (ApprovalRoute.mod.cs) の Load に共通化してある。

// 経路を組み立てる (Approval フィールドの「経路組み立て」に設定。null を返すと申請は中止され、保存もされない)
ApprovalRouteData OnBuildRoute()
{
    //申請時のデータ正当性チェック (フィールド必須は IsRequired、複合条件・業務ルールはここ)
    if (Amount.Value == null || Amount.Value <= 0)
    {
        Logger.Error("金額は 1 円以上を入力してください");
        return null;
    }

    //金額で経路マスタの経路を選ぶ。10 万円以上は部長承認が挟まる「高額経費ルート」
    var routeName = Amount.Value >= 100000 ? "高額経費ルート" : "経費ルート";
    return new ApprovalRoute().Load(routeName);
}

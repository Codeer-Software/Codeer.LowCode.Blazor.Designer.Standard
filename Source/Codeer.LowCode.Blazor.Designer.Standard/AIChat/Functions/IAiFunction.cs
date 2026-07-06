namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // AI チャットの「機能(単一責務)」の抽象。
    // 従来は画面ごとに 1 つの巨大な Chat が複数責務を 1 プロンプトに混載していたが、
    // これを責務単位(フィールド作成/フィールド編集/詳細レイアウト編集/スクリプト編集/…)に割り、
    // 画面をまたいで再利用できるユニットにする。初段ルーターが意図を機能に分解し、
    // オーケストレーターが対応機能だけを順番に実行する。
    internal interface IAiFunction
    {
        // 機能の一意 ID(例: "field.create")。ルーターのプラン・画面の対応機能セットで使う。
        string Id { get; }

        // ユーザー向け表示名(例: "フィールド作成")。
        string DisplayName { get; }

        // ルーターが「この指示をこの機能に振るべきか」を判断するための説明。
        // 何ができて何ができないかを簡潔に書く。
        string RouterDescription { get; }

        // ルーターが割り当てた 1 機能ぶんの指示を実行する。
        Task<FunctionResult> ExecuteAsync(string instruction);

        // 会話履歴をリセットする(機能ごとに履歴を保持しているため)。
        void Clear();
    }

    internal enum FunctionOutcome
    {
        // 変更を適用した。
        Done,
        // 何もしなかった(できない依頼の断り・現状維持を含む)。
        NothingToDo,
        // 実行中にエラーが発生した(適用していない)。
        Error,
    }

    // 1 機能の実行結果。オーケストレーターが複数機能の結果を集約してユーザーへ返す。
    internal sealed class FunctionResult
    {
        public FunctionOutcome Outcome { get; }
        public string Message { get; }

        FunctionResult(FunctionOutcome outcome, string message)
        {
            Outcome = outcome;
            Message = message ?? string.Empty;
        }

        public static FunctionResult Done(string message) => new(FunctionOutcome.Done, message);
        public static FunctionResult NothingToDo(string message) => new(FunctionOutcome.NothingToDo, message);
        public static FunctionResult Error(string message) => new(FunctionOutcome.Error, message);
    }
}

using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// アセンブリ全体の初期化。実アプリ (App.xaml.cs) と同じく「起動時に一度」拡張ライブラリを登録する。
    ///
    /// フィールド型の列挙結果はプロセス内にキャッシュされるため、どれかのテストが先にフィールド型を
    /// 列挙してしまうと、後から Extras をロードしてもその型がカタログに載らない (単独実行では通るのに
    /// フルランでは Extras フィールドが選ばれない、という実行順依存になる)。
    /// 個々のフィクスチャの OneTimeSetUp では遅いので、全テストより前のここで登録する。
    /// </summary>
    [SetUpFixture]
    public class TestSetup
    {
        [OneTimeSetUp]
        public void RegisterExtras()
        {
            // CSS は不要なので引数なし版でよい。
#pragma warning disable CS0618
            Codeer.LowCode.Blazor.Extras.Designer.ExtrasDesignerInitializer.Initialize();
#pragma warning restore CS0618
        }
    }
}

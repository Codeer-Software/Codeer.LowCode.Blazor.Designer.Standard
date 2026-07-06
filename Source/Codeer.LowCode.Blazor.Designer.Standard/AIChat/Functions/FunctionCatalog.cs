namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // 全機能の ID・表示名・説明・「どの画面で使えるか」を集約したカタログ。
    // ・ルーターへ渡す機能一覧の説明文。
    // ・対応外機能を断るとき「その機能は○○画面で行えます」の案内先。
    // 画面 → 機能の対応表(ユーザー確定版)もここに持つ。
    internal static class FunctionCatalog
    {
        public const string FieldCreate = "field.create";
        public const string FieldEdit = "field.edit";
        public const string ModuleSettings = "module.settings";
        public const string LayoutDetail = "layout.detail";
        public const string LayoutList = "layout.list";
        public const string LayoutSearch = "layout.search";
        public const string ScriptEdit = "script.edit";
        public const string SqlEdit = "sql.edit";
        public const string SqlFieldSettings = "sql.field.settings";
        public const string PageFrameEdit = "pageframe.edit";
        public const string CssEdit = "css.edit";
        public const string DbUpdate = "db.update";

        public sealed class Entry
        {
            public string Id { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
            public string RouterDescription { get; init; } = string.Empty;
        }

        // 機能メタ情報(表示名・ルーター向け説明)。個々の Function 実装の DisplayName/RouterDescription はこれを参照する。
        public static readonly IReadOnlyDictionary<string, Entry> Entries = new Dictionary<string, Entry>
        {
            [FieldCreate] = new()
            {
                Id = FieldCreate, DisplayName = "フィールド作成",
                RouterDescription = "モジュールに新しいフィールド(項目)を追加する。作成と同時にイベント(OnClick等)の設定も可(参照する空ハンドラ関数はスクリプトへ自動作成される)。既存フィールドのプロパティ変更や削除は行わない。",
            },
            [FieldEdit] = new()
            {
                Id = FieldEdit, DisplayName = "フィールド編集",
                RouterDescription = "既存フィールドのプロパティ(表示名・最大長・最大値・必須・候補・検索条件など)の変更、イベント(OnClick等)の設定/解除(参照する空ハンドラ関数はスクリプトへ自動作成される。処理の中身は script.edit)、フィールドのリネーム、フィールドの削除を行う。新規追加は行わない。",
            },
            [ModuleSettings] = new()
            {
                Id = ModuleSettings, DisplayName = "モジュール設定編集",
                RouterDescription = "モジュール全体の設定(データソース・DBテーブル・作成/更新/削除の可否・ユーザー/データの読み書き権限条件・DDL生成)を変更する。フィールドの追加/編集そのものは扱わない。",
            },
            [LayoutDetail] = new()
            {
                Id = LayoutDetail, DisplayName = "詳細レイアウト編集",
                RouterDescription = "詳細画面のレイアウト(フィールドの配置・グリッド・タブ・罫線・見出しラベル・タイトル/戻る/サブミット)を編集する。フィールドそのものの追加/プロパティ編集はしない。",
            },
            [LayoutList] = new()
            {
                Id = LayoutList, DisplayName = "一覧レイアウト編集",
                RouterDescription = "一覧画面のレイアウト(列の並び・表示項目・幅など)を編集する。フィールドそのものの追加/プロパティ編集はしない。",
            },
            [LayoutSearch] = new()
            {
                Id = LayoutSearch, DisplayName = "検索レイアウト編集",
                RouterDescription = "検索画面のレイアウト(検索項目の配置)を編集する。フィールドそのものの追加/プロパティ編集はしない。",
            },
            [ScriptEdit] = new()
            {
                Id = ScriptEdit, DisplayName = "スクリプト編集",
                RouterDescription = "モジュールのC#ライクなスクリプト(イベントハンドラ・式)を編集する。",
            },
            [SqlEdit] = new()
            {
                Id = SqlEdit, DisplayName = "SQL編集",
                RouterDescription = "ExecuteSql/Query フィールドの SQL を対話しながら作成・改善する。",
            },
            [SqlFieldSettings] = new()
            {
                Id = SqlFieldSettings, DisplayName = "SQLフィールド設定編集",
                RouterDescription = "この画面の ExecuteSql/Query フィールド自身の設定(実行タイミング・入出力・その他プロパティ)を変更する。SQL文の作成・改善は sql.edit で行う。他のフィールドは扱わない。",
            },
            [PageFrameEdit] = new()
            {
                Id = PageFrameEdit, DisplayName = "ページフレーム編集",
                RouterDescription = "ページフレーム(ナビゲーション構造・サイドバー・ヘッダー・権限条件など)を編集する。",
            },
            [CssEdit] = new()
            {
                Id = CssEdit, DisplayName = "css編集",
                RouterDescription = "アプリケーションのCSSを編集する。",
            },
            [DbUpdate] = new()
            {
                Id = DbUpdate, DisplayName = "DB更新(DDL生成)",
                RouterDescription = "現在のモジュール定義に合わせて物理DBのスキーマを変更するDDL(CREATE/ALTER/DROP/INDEX等)を生成し提示する。ユーザーが『DBにテーブルを作成して』『DBに反映して』『ALTERして』『テーブル定義を変えて』『DBを更新して』『列をDBからも削除して』等、**物理DB(テーブル・列)への反映**を求めたときに使う(フィールド定義の変更とセットで頼まれることが多い→フィールド作成/編集の後に db.update を続ける)。",
            },
        };

        // 画面名(ユーザー向け表示)。対応外機能の案内で「○○画面で行えます」に使う。
        public const string ScreenOverallSettings = "全体設定";
        public const string ScreenDetailLayout = "詳細レイアウト";
        public const string ScreenListLayout = "一覧レイアウト";
        public const string ScreenSearchLayout = "検索レイアウト";
        public const string ScreenScript = "スクリプト";
        public const string ScreenSql = "SQL";
        public const string ScreenPageFrame = "ページフレーム";
        public const string ScreenCss = "css";

        // 画面 → その画面が対応する機能 ID 群(ユーザー確定版のマップ)。
        public static readonly IReadOnlyDictionary<string, string[]> ScreenFunctions = new Dictionary<string, string[]>
        {
            [ScreenOverallSettings] = new[] { ModuleSettings, FieldCreate, FieldEdit, DbUpdate },
            [ScreenDetailLayout] = new[] { FieldCreate, FieldEdit, LayoutDetail },
            [ScreenListLayout] = new[] { FieldCreate, FieldEdit, LayoutList },
            [ScreenSearchLayout] = new[] { FieldEdit, LayoutSearch },
            [ScreenScript] = new[] { ScriptEdit },
            [ScreenSql] = new[] { SqlEdit, SqlFieldSettings },
            [ScreenPageFrame] = new[] { PageFrameEdit },
            [ScreenCss] = new[] { CssEdit },
        };

        // 指定機能を実行できる画面の表示名一覧(対応外の案内文用)。
        public static IReadOnlyList<string> ScreensFor(string functionId)
            => ScreenFunctions.Where(kv => kv.Value.Contains(functionId)).Select(kv => kv.Key).ToList();
    }
}

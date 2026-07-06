using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Microsoft.Extensions.AI;
using System.Data;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using static Codeer.LowCode.Blazor.Designer.Standard.AIChat.DbDefinitionServiceExtensions;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // SQL編集機能(ExecuteSqlField / QueryField)。初期コンテキストは3モード:
    //
    // 1. {データソース名}.txt (ヒントファイル)がある → それを正としてテーブル選択フローで進める。
    //    既存アプリの巨大DBに接続するケースでは全スキーマは渡せない/渡すと薄まるため、
    //    人間がキュレーションした txt (使うべきテーブル・業務的な意味) を中心に組み立てる。
    //    全スキーマの読み込みはしない。列の詳細は選択されたテーブル分だけ提供する。
    // 2. txt が無く、スキーマ全文が FullSchemaMaxLength 以下 → 全スキーマ一括モード。
    //    全テーブルの列定義(+ExecuteSqlは function/procedure/package のシグネチャ)を最初から渡し、
    //    1ターンでSQL生成まで到達できるようにする。
    // 3. txt が無く、スキーマが巨大 → 自動生成のテーブル名一覧でテーブル選択フローへフォールバック。
    //
    // 生成されたSQLは適用前に検証する(ValidateSqlAsync)。Query(SELECT)は実DBに対して
    // スキーマ取得のみの実行チェックを行い、エラーはAIへ自動で差し戻して修正させる。
    internal abstract class SqlFunctionBase : IAiFunction
    {
        // スキーマ全文(文字数)がこの値以下なら全スキーマ一括モード。超えたらテーブル選択フェーズへフォールバック。
        internal const int FullSchemaMaxLength = 100_000;

        // 1回のユーザー指示に対する SQL 生成の最大試行回数(初回 + 検証エラーの自動修正)。
        internal const int MaxSqlAttempts = 3;

        protected readonly IDesignerChatHost _host;
        protected readonly IChatClient _chatClient;
        protected readonly string _dataSourceName;
        protected readonly List<ChatMessage> _chatHistory = new();
        bool _selectionFlow;          //テーブル選択フロー(ヒントtxtあり or 巨大スキーマ)か
        bool _initialInfoIsAutoList;  //履歴[1]が自動生成のテーブル名一覧か(選択後に削る。人手ヒントは残す)
        bool _writingPhaseStarted;
        readonly HashSet<string> _selectedTables = new(StringComparer.OrdinalIgnoreCase);

        public string Id => FunctionCatalog.SqlEdit;
        public string DisplayName => FunctionCatalog.Entries[FunctionCatalog.SqlEdit].DisplayName;
        public string RouterDescription => FunctionCatalog.Entries[FunctionCatalog.SqlEdit].RouterDescription;

        protected SqlFunctionBase(IDesignerChatHost host, string dataSourceName, IChatClient chatClient)
        {
            _host = host;
            _dataSourceName = dataSourceName;
            _chatClient = chatClient;
        }

        // ==== 派生クラスが与える差分 ====

        // $$$reset$$$ を受けたときのユーザー向けメッセージ(文言が僅かに異なる)。
        protected abstract string ResetMessage { get; }

        // 現在の SQL(改善対象)を取得する(エディタ型が異なるため派生でフォワード)。
        protected abstract string GetCurrentSql();

        // 生成された SQL とパラメータをエディタへ適用する(エディタ型が異なるため派生でフォワード)。
        protected abstract void ApplySqlAndParameters(string sql, List<DbParameterSetting> dbParams);

        // 全スキーマ一括モードで渡すスキーマ全文(全テーブルの列定義。ExecuteSql は extra 定義も含む)。
        protected abstract string BuildFullSchemaText(List<DbTableDefinition> info);

        // 全スキーマ一括モードのシステムプロンプト。
        protected abstract ChatMessage BuildFullModeSystemMessage(string dbType, string currentSqlMessage);

        // ==== フォールバック(テーブル選択2フェーズ)用の差分 ====

        // データソース情報の要約(テーブル名一覧等)を組み立てる。
        protected abstract string BuildDataSourceInfoFromDbInfo(List<DbTableDefinition> info);

        // テーブル選択フェーズの指示プロンプト。
        protected abstract ChatMessage BuildTableSelectionMessage(string dbType, string currentSqlMessage);

        // データソース情報(テーブル一覧等)を提供するメッセージ。
        protected abstract ChatMessage BuildDataSourceInfoMessage(string dataSourceInfo);

        // SQL記述フェーズの指示プロンプト。
        protected abstract ChatMessage BuildSqlWritingMessage();

        // 選択されたテーブル(等)の詳細定義を提供するメッセージ。
        protected abstract ChatMessage BuildSelectedTableInfoMessage(List<DbTableDefinition> info, List<string> selectedTables);

        // 適用前のSQL検証。Ran=false は「検証できる環境がない」(スキップ、従来通り適用)。
        // Ran=true かつ Error!=null なら AI へ差し戻して修正させる。
        protected virtual Task<(bool Ran, string? Error)> ValidateSqlAsync(string sql, List<DbParameterSetting> dbParams)
            => Task.FromResult<(bool, string?)>((false, null));

        // ==== 共通フロー ====

        public virtual void Clear()
        {
            _chatHistory.Clear();
            if (string.IsNullOrEmpty(_dataSourceName)) return;

            var info = _host.GetDbInfo(_dataSourceName);
            var hint = LoadHintText();
            var dataSource = _host.GetDataSources().FirstOrDefault(x => x.Name == _dataSourceName);
            var dbType = dataSource?.DataSourceType.ToString() ?? string.Empty;

            var currentSqlMessage = string.Empty;
            var currentSql = GetCurrentSql();
            if (!string.IsNullOrEmpty(currentSql))
            {
                currentSqlMessage = $@"現在のSQLです。これの改善を求められることもあります。
{currentSql}";
            }

            _selectionFlow = false;
            _initialInfoIsAutoList = false;
            _writingPhaseStarted = false;
            _selectedTables.Clear();

            //ヒントtxtがあるならそれを正としてテーブル選択フローで進める(巨大DB前提の人手キュレーション)。
            //全スキーマの構築(ExecuteSqlはDBへの extra 定義取得を含む)もしない。
            if (!string.IsNullOrEmpty(hint))
            {
                _selectionFlow = true;
                _chatHistory.Add(BuildTableSelectionMessage(dbType, currentSqlMessage));
                _chatHistory.Add(BuildDataSourceInfoMessage(hint));
                return;
            }

            var fullSchema = BuildFullSchemaText(info);
            if (fullSchema.Length <= FullSchemaMaxLength)
            {
                _chatHistory.Add(BuildFullModeSystemMessage(dbType, currentSqlMessage));
                _chatHistory.Add(new ChatMessage(ChatRole.System, ComposeSchemaMessage(fullSchema)));
            }
            else
            {
                _selectionFlow = true;
                _initialInfoIsAutoList = true;
                _chatHistory.Add(BuildTableSelectionMessage(dbType, currentSqlMessage));
                _chatHistory.Add(BuildDataSourceInfoMessage(BuildDataSourceInfoFromDbInfo(info)));
            }
        }

        // {データソース名}.txt = 人間が書くヒントファイル(任意)。テーブルの業務的な意味・使い分け等。
        string LoadHintText()
        {
            try
            {
                var path = Path.Combine(_host.CurrentFileDirectory, $"{_dataSourceName}.txt");
                return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        //自信が無いのに作る(もっともらしい嘘SQL)のを止めるためのゲート。
        //生の確度自己申告は当てにならないため、「80%未満とみなすべき症状」を具体的に列挙して判定させる。
        protected static string BuildConfidenceGateSection() => @"
## 作成前の自己チェック(重要)
自信が無いのに作らないでください。次のいずれかに当てはまるときは「確度80%未満」とみなし、
SQLを出力せず、ユーザーに確認を取ってください:
- 要求に合うテーブル・列の候補が複数あって決め手がない
- 要求に出てくる業務用語がどのテーブル・列に対応するか推測になっている
- 要求されたデータが与えられた定義の中に見当たらない(それらしい別物で代用しようとしている)
- 絞り込み条件・集計方法・期間などの解釈が複数ありえる
確認するときは「あなたの解釈」「使う予定のテーブル・列」「確認したい点」を簡潔に箇条書きで示し、
ユーザーの回答を得てから作成してください。
明確に判断できる場合(確度80%以上)だけ、確認を挟まず作成して構いません。
";

        static string ComposeSchemaMessage(string fullSchema)
        {
            var sb = new StringBuilder();
            sb.AppendLine("データベースの定義情報です。ここに存在するテーブル・列・定義だけを使ってください。");
            sb.AppendLine(fullSchema);
            return sb.ToString();
        }

        //```tbl``` で出力された選択テーブルを登録し、定義提供メッセージを追加する。
        //何度でも呼べる(途中でテーブルを追加できる)。選択内容に変化が無ければ false(再問い合わせしない)。
        bool RegisterSelectedTables(List<string> tables)
        {
            var anyNew = false;
            foreach (var t in tables.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                anyNew |= _selectedTables.Add(t);
            }

            var info = _host.GetDbInfo(_dataSourceName);

            if (!_writingPhaseStarted)
            {
                _writingPhaseStarted = true;
                //自動生成のテーブル名一覧は選択後は不要(詳細が提供される)なので削る。
                //人手のヒントtxtは業務情報を含むためSQL記述中も残す。
                if (_initialInfoIsAutoList) _chatHistory.RemoveAt(1);
                _chatHistory.Add(BuildSqlWritingMessage());
                _chatHistory.Add(BuildSelectedTableInfoMessage(info, _selectedTables.ToList()));
                return true;
            }

            if (!anyNew) return false;
            _chatHistory.Add(BuildSelectedTableInfoMessage(info, _selectedTables.ToList()));
            return true;
        }

        protected static string CreateDbInfo(List<DbTableDefinition> info, List<string> selectedTables)
        {
            var tables = new List<string>();
            foreach (var table in FilterTables(info, selectedTables))
            {
                var columns = new List<string>();
                foreach (var column in table.Columns)
                {
                    columns.Add($"{column.Name}:{column.RawDbTypeName}{(column.IsNullable ? "" : " NOT NULL")}");
                }

                tables.Add($"{table.Name}:{{{string.Join(",", columns)}}}");
            }
            return string.Join("\n", tables);
        }

        protected static List<DbTableDefinition> FilterTables(List<DbTableDefinition> info, List<string> selectedTables)
        {
            var set = new HashSet<string>(selectedTables, StringComparer.OrdinalIgnoreCase);
            return info.Where(e => set.Contains(e.Name)).ToList();
        }

        protected static string CreateFullDbInfo(List<DbTableDefinition> info)
            => CreateDbInfo(info, info.Select(e => e.Name).ToList());

        public async Task<FunctionResult> ExecuteAsync(string userMessage)
        {
            if (!_chatHistory.Any()) Clear();

            if (string.IsNullOrEmpty(_dataSourceName)) return FunctionResult.NothingToDo("データソースが指定されていません。");

            _chatHistory.Add(new ChatMessage(ChatRole.User, userMessage));
            var result = await _chatClient.GetResponseAsync(_chatHistory);
            var resultText = result.Text;
            _chatHistory.Add(new ChatMessage(ChatRole.Assistant, resultText));

            if (resultText.Contains("$$$reset$$$"))
            {
                Clear();
                return FunctionResult.NothingToDo(ResetMessage);
            }

            var matchTable = Regex.Match(resultText, @"```tbl\s(.*?)\s```", RegexOptions.Singleline);
            if (matchTable.Success && _selectionFlow)
            {
                var tables = matchTable.Groups[1].Value.Split(',').Select(e => e.Trim()).ToList();
                //選択内容に変化があるときだけ定義を提供して続きを問い合わせる(同一選択の繰り返しによる無限再帰防止)
                if (RegisterSelectedTables(tables))
                {
                    var inner = await ExecuteAsync(userMessage);
                    var combined = resultText + "\r\n" + inner.Message;
                    return inner.Outcome switch
                    {
                        FunctionOutcome.Done => FunctionResult.Done(combined),
                        FunctionOutcome.Error => FunctionResult.Error(combined),
                        _ => FunctionResult.NothingToDo(combined),
                    };
                }
            }

            var (sql, dbParams, cleanedText, parseError) = ExtractSqlAndParameters(resultText);
            if (parseError != null) return FunctionResult.Error(parseError);
            if (string.IsNullOrEmpty(sql)) return FunctionResult.Done(cleanedText);

            // 検証ループ: エラーはユーザーに middleman をさせず AI へ自動で差し戻す。
            var (ran, error) = await ValidateSqlAsync(sql, dbParams);
            var attempts = 1;
            while (ran && error != null && attempts < MaxSqlAttempts)
            {
                attempts++;
                _chatHistory.Add(new ChatMessage(ChatRole.User, $@"作成されたSQLをデータベースで実行検証したところエラーになりました。
エラー: {error}
修正した SQL と schema を再出力してください。説明は不要です。"));
                var retry = await _chatClient.GetResponseAsync(_chatHistory);
                _chatHistory.Add(new ChatMessage(ChatRole.Assistant, retry.Text));

                var (sql2, dbParams2, cleaned2, parseError2) = ExtractSqlAndParameters(retry.Text);
                if (parseError2 != null || string.IsNullOrEmpty(sql2)) break; //修正が取れなければ直前のSQLを使う

                sql = sql2;
                dbParams = dbParams2;
                cleanedText = cleaned2;
                (ran, error) = await ValidateSqlAsync(sql, dbParams);
            }

            ApplySqlAndParameters(sql, dbParams);

            var note = !ran
                ? "作成しました、ご確認お願いします。"
                : error == null
                    ? "作成しました(データベースでの実行チェックOK)、ご確認お願いします。"
                    : $@"作成して適用しましたが、実行チェックのエラーが解消できていません。ご確認ください。
エラー: {error}";
            return FunctionResult.Done(string.Join(Environment.NewLine, cleanedText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
                + "\r\n" + note);
        }

        // 応答テキストから ```sql``` と ```schema``` を抽出する。
        (string Sql, List<DbParameterSetting> Params, string CleanedText, string? ParseError) ExtractSqlAndParameters(string resultText)
        {
            var matchSql = Regex.Match(resultText, @"```sql\s(.*?)\s```", RegexOptions.Singleline);
            var sql = string.Empty;
            if (matchSql.Success)
            {
                sql = matchSql.Groups[1].Value;
                resultText = Regex.Replace(resultText, @"(?s)```sql.*?```", string.Empty, RegexOptions.IgnoreCase);
            }

            var dbParams = new List<DbParameterSetting>();
            var matchParams = Regex.Match(resultText, @"```schema\s*(\[.*?\])\s*```", RegexOptions.Singleline);
            if (matchParams.Success)
            {
                try
                {
                    dbParams = JsonConverterEx.DeserializeObject<List<DbParameterSetting>>(matchParams.Groups[1].Value) ?? new();
                }
                catch
                {
                    return (string.Empty, new(), resultText, "パラメータ定義(schema)の解析に失敗しました。もう一度作成を依頼してください。");
                }
                resultText = Regex.Replace(resultText, @"(?s)```schema.*?```", string.Empty, RegexOptions.IgnoreCase);
            }

            return (sql, dbParams, resultText, null);
        }

        // 検証時にバインドすべきパラメータ名。宣言されたパラメータに加え、
        // SQL 中で参照される CLB のシステムページングパラメータ(rows_per_page / offset)も対象にする。
        internal static List<string> CollectBindParameterNames(string sql, List<DbParameterSetting> dbParams)
        {
            var names = dbParams
                .Where(p => p.IsParameter && !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => p.Name.Trim())
                .ToList();

            foreach (var sys in new[] { DbParameterSetting.ROWS_PER_PAGE_PARA, DbParameterSetting.OFFSET })
            {
                var m = Regex.Match(sql, $@"[@:]{sys}\b", RegexOptions.IgnoreCase);
                if (m.Success && !names.Any(n => n.TrimStart('@', ':').Equals(sys, StringComparison.OrdinalIgnoreCase)))
                {
                    names.Add(m.Value);
                }
            }
            return names;
        }
    }

    // ExecuteSql フィールドの SQL 編集。旧 ExecuteSqlChat。
    // テーブルに加えて function/procedure/package も対象にできる。
    internal class ExecuteSqlFunction : SqlFunctionBase
    {
        readonly IExecuteSqlEditor _editor;
        DbExtraDefinitions? _extraDefinitionsCache;

        public ExecuteSqlFunction(IDesignerChatHost host, Func<IChatClient> createChatClient, IExecuteSqlEditor editor)
            : base(host, ValidateAndGetDataSourceName(editor), createChatClient())
        {
            _editor = editor;
        }

        // 旧 ExecuteSqlChat のコンストラクタと同じ検証を行い、データソース名を返す。
        // base(...) の引数は左から順に評価されるため、この検証が通ってから CreateChatClient() が呼ばれる。
        static string ValidateAndGetDataSourceName(IExecuteSqlEditor editor)
        {
            if (editor == null) throw new ArgumentNullException(nameof(editor));
            return editor.GetDataSourceName();
        }

        public override void Clear()
        {
            _extraDefinitionsCache = null; //セッション中のDB変更に追従できるよう、リセット時に取り直す
            base.Clear();
        }

        protected override string ResetMessage => "現在選択中のテーブル等の情報では実現できませんでした。もう一度最初からやり直してください。";

        protected override string GetCurrentSql() => _editor.GetCurrentSql();

        protected override void ApplySqlAndParameters(string sql, List<DbParameterSetting> dbParams)
            => _editor.ApplySqlAndParameters(sql, dbParams);

        // パラメータ値はモジュールのフィールドからバインドされるため、使えるフィールド名を教える。
        string BuildParameterSourceSection()
        {
            List<string> fields;
            try
            {
                fields = _editor.GetModuleDesign().Fields
                    .OfType<DbValueFieldDesignBase>()
                    .Select(f => f.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
            }
            catch
            {
                return string.Empty;
            }
            if (fields.Count == 0) return string.Empty;
            return $@"
パラメータの値は実行時にモジュールの同名フィールドからバインドされます。パラメータ名は可能な限り次のフィールド名に合わせてください:
{string.Join(", ", fields)}";
        }

        protected override string BuildFullSchemaText(List<DbTableDefinition> info)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Tables:");
            sb.AppendLine(CreateFullDbInfo(info));

            //function/procedure/package はパラメータ定義付きシグネチャを全件渡す(本文は含まないため軽い)
            var detail = GetExtraDefinitionsCached().GetDetailedDefiniations(new List<string>());
            if (!string.IsNullOrEmpty(detail))
            {
                sb.AppendLine();
                sb.AppendLine(detail);
            }
            return sb.ToString();
        }

        protected override ChatMessage BuildFullModeSystemMessage(string dbType, string currentSqlMessage)
            => new ChatMessage(ChatRole.System, $@"
あなたはSQLの専門家です。
ユーザーが求めるSQLを書くことが目的です。
ユーザーの使っているDBの種類は
{dbType} です。

{currentSqlMessage}

別のメッセージでこのデータベースの全テーブル定義と function/procedure/package の定義を提供します。
そこに存在するものだけを使ってください。存在しないテーブルや列を推測で使うのは厳禁です。
要求が明確なら確認を挟まず一度でSQLまで出してください。不明点がある場合だけ質問してください。

SQLの部分は以下のように囲ってください。

```sql

```
このSQLはC#からDbConnectionを使って実行します。DECLAREなどその方法で使えないSQLは入れないでください。パラメータをユーザーに求められた場合はその前提で書いてください。
またパラメータは指定しない場合はその条件を無視するようなSQLにしてください。
例えば以下のようなものです。
saledate >= @p1 OR @p1 IS NULL

最終的にパラメータは必ず以下の形でも出力してください。
パラメータはIsParameterをtrueにしてNameは@や:などを含めた形で書いてください。
DbParameterDirectionはユーザの要求或いはDBの定義によってInput,Output,InputOutput,ReturnValueから選択してください。

```schema
[
  {{
    ""Name"": ""name"",
    ""DbType"": ""db raw type""
  }},
  {{
    ""IsParameter"": true,
    ""Name"": ""@name"",
    ""DbType"": ""db raw type"",
    ""DbParameterDirection"": ""by db definition or user specified""
  }}
]
```

C#の解説は必要ありません。
作成したSQLがエラーだったと伝えられた場合は、言い訳を書かずに簡潔に修正した SQL と schema を返してください。
{BuildConfidenceGateSection()}{BuildParameterSourceSection()}
");

        protected override string BuildDataSourceInfoFromDbInfo(List<DbTableDefinition> info)
        {
            var extraInfo = GetExtraDefinitionsCached().ToString();

            var sb = new StringBuilder();
            foreach (var e in info)
            {
                sb.Append($"{e.Name}\n");
            }
            sb.Append(extraInfo); //Function,ストアド、パッケージの名前情報
            return sb.ToString();
        }

        protected override ChatMessage BuildTableSelectionMessage(string dbType, string currentSqlMessage)
            => new ChatMessage(ChatRole.System, $@"
あなたはSQLの専門家です。
ユーザーが求めるSQLを書くことが目的です。
ユーザーの使っているDBの種類は
{dbType} です。

{currentSqlMessage}

まずSQLで使うテーブル、function, procedure, package(これらをDBターゲットと言います)を決めます。決め方の優先順:
1. ユーザーがチャットで使うDBターゲットを指定・示唆した場合はそれに従う(最優先)
2. ユーザーの要求から適切なDBターゲットをあなたが判断できる場合は、確認を待たずに選択してよい
3. 判断に迷う場合(候補が複数ある・情報が足りない)だけ、候補を挙げてユーザーに質問して選んでもらう

使うDBターゲットが決まったら、以下の囲いの中に名前をカンマ区切りで出力してください。

```tbl

```

この出力があると各DBターゲットの定義が提供されるので、それを見てSQLを書きます。
定義の提供前にSQLを書かないでください(列名や引数の推測は厳禁です)。
後からDBターゲットを追加したくなった場合は、再度 ```tbl``` の囲いで追加分を含めて出力してください。
{BuildConfidenceGateSection()}{BuildParameterSourceSection()}
");

        protected override ChatMessage BuildDataSourceInfoMessage(string dataSourceInfo)
            => new ChatMessage(ChatRole.System, $@"
テーブル及びfunction/procedure/package情報です。
{dataSourceInfo}
");

        protected override ChatMessage BuildSqlWritingMessage()
            => new ChatMessage(ChatRole.System, $@"
今からはSQLを書くフェーズです。
引き続き、ユーザーと話しつつクエリの設計を進めてください。
SQLを作る以外にも質問があれば答えてください。
SQLの部分は以下のように囲ってください。

```sql

```
このSQLはC#からDbConnectionを使って実行します。DECLAREなどその方法で使えないSQLは入れないでください。パラメータをユーザーに求められた場合はその前提で書いてください。
またパラメータは指定しない場合はその条件を無視するようなSQLにしてください。
例えば以下のようなものです。
saledate >= @p1 OR @p1 IS NULL

最終的にパラメータは必ず以下の形でも出力してください。
パラメータはIsParameterをtrueにしてNameは@や:などを含めた形で書いてください。
DbParameterDirectionはユーザの要求或いはDBの定義によってInput,Output,InputOutput,ReturnValueから選択してください。

```schema
[
  {{
    ""Name"": ""name"",
    ""DbType"": ""db raw type""
  }},
  {{
    ""IsParameter"": true,
    ""Name"": ""@name"",
    ""DbType"": ""db raw type"",
    ""DbParameterDirection"": ""by db definition or user specified""
  }}
]
```

C#の解説は必要ありません。

たまにあなたが作ったSQLを実行すると失敗するときがあります。
その場合はユーザーはあなたにその旨を伝え作り直しを要求します。
その場合でも言い訳は書かなくていいので簡潔にシンプルに迅速にSQLとスキーマを返してください。

与えられた定義に無いDBターゲットが必要になった場合や、ユーザーが別のDBターゲットを指定した場合は、
再度 ```tbl``` の囲いで必要な名前(追加分を含む)を出力してください。定義が提供されます。
定義の無いDBターゲットの列名や引数を推測で書くのは厳禁です。
{BuildConfidenceGateSection()}{BuildParameterSourceSection()}
");

        protected override ChatMessage BuildSelectedTableInfoMessage(List<DbTableDefinition> info, List<string> selectedTables)
            => new ChatMessage(ChatRole.System, $@"
現在選択されているDBターゲットの定義です。ここに無いDBターゲットの列名や引数を推測で使わないでください。
他のDBターゲットが必要になったら、再度 ```tbl``` の囲いで名前を出力すれば定義が提供されます。
{CreateDbInfo(info, selectedTables)}。
{GetExtraDefinitionsCached().GetDetailedDefiniations(selectedTables)}//選択されたfunction,ストアド、パッケージの詳細定義
");

        DbExtraDefinitions GetExtraDefinitionsCached()
        {
            if (_extraDefinitionsCache != null) return _extraDefinitionsCache;

            DbExtraDefinitions? extraInfo = null;
            var thread = new Thread(() => extraInfo = GetDbGetExtraDefinitionsAsync().Result);
            thread.Start();
            thread.Join();
            _extraDefinitionsCache = extraInfo ?? new();
            return _extraDefinitionsCache;
        }

        async Task<DbExtraDefinitions> GetDbGetExtraDefinitionsAsync()
        {
            IDbAccessor? dbAccessor = null;
            try
            {
                dbAccessor = _editor.CreateDbAccessor();
                return await GetExtraDefinitionsAsync(dbAccessor, _editor.GetDataSourceName());
            }
            catch
            {
                return new();
            }
            finally
            {
                if (dbAccessor != null) await dbAccessor.DisposeAsync();
            }
        }
    }

    // Query フィールドの SQL 編集。旧 QueryChat。テーブルのみを対象にする。
    // 生成した SELECT は適用前に実DBでスキーマ取得のみの実行チェックを行い、エラーは AI に自動修正させる。
    internal class QueryFunction : SqlFunctionBase
    {
        readonly IQueryEditor _editor;

        public QueryFunction(IDesignerChatHost host, Func<IChatClient> createChatClient, IQueryEditor editor)
            : base(host, editor.GetDataSourceName(), createChatClient())
        {
            _editor = editor;
        }

        protected override string ResetMessage => "現在選択中のテーブル情報では実現できませんでした。もう一度最初からやり直してください。";

        protected override string GetCurrentSql() => _editor.GetCurrentSql();

        protected override void ApplySqlAndParameters(string sql, List<DbParameterSetting> dbParams)
            => _editor.ApplySqlAndParameters(sql, dbParams);

        // Query の結果はモジュールのフィールドへ「SELECT出力列名 = DbColumn」の一致でマッピングされるため、
        // 合わせるべき列名を教える(合っていないと SQL は動くのに画面に値が出ない)。
        string BuildModuleMappingSection()
        {
            List<string> columns;
            string moduleName;
            try
            {
                var module = _editor.GetModuleDesign();
                moduleName = module.Name;
                columns = module.Fields
                    .OfType<DbValueFieldDesignBase>()
                    .Select(f => f.DbColumn)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct()
                    .ToList();
            }
            catch
            {
                return string.Empty;
            }
            if (columns.Count == 0) return string.Empty;
            return $@"
このSQLの結果はモジュール '{moduleName}' のフィールドへ「SELECTの出力列名と DbColumn の一致」でマッピングされます。
特に指定が無い限り、SELECTの出力列名(別名)は次に合わせてください:
{string.Join(", ", columns)}";
        }

        protected override string BuildFullSchemaText(List<DbTableDefinition> info)
            => CreateFullDbInfo(info);

        protected override ChatMessage BuildFullModeSystemMessage(string dbType, string currentSqlMessage)
            => new ChatMessage(ChatRole.System, $@"
あなたはSQLの専門家です。
ユーザーが求めるSQL(SELECT)を書くことが目的です。
ユーザーの使っているDBの種類は
{dbType} です。

{currentSqlMessage}

別のメッセージでこのデータベースの全テーブル定義を提供します。
そこに存在するテーブル・列だけを使ってください。存在しないものを推測で使うのは厳禁です。
要求が明確なら確認を挟まず一度でSQLまで出してください。不明点がある場合だけ質問してください。

SQLの部分は以下のように囲ってください。

```sql

```
このSQLはC#からDbConnectionを使って実行します。DECLAREなどその方法で使えないSQLは入れないでください。パラメータをユーザーに求められた場合はその前提で書いてください。
またパラメータは指定しない場合はその条件を無視するようなSQLにしてください。
例えば以下のようなものです。
saledate >= @p1 OR @p1 IS NULL

最終的にSelectされる項目とパラメータは以下の形でも出力してください。
カラムはIsParameterを出力しないでください。
パラメータはIsParameterをtrueにしてNameは@や:などを含めた形で書いてください。
```schema
[
  {{
    ""Name"": ""name"",
    ""DbType"": ""db raw type""
  }},
  {{
    ""IsParameter"": true,
    ""Name"": ""@name"",
    ""DbType"": ""db raw type""
  }}
]
```

C#の解説は必要ありません。
作成したSQLがエラーだったと伝えられた場合は、言い訳を書かずに簡潔に修正した SQL と schema を返してください。
{BuildConfidenceGateSection()}{BuildModuleMappingSection()}
");

        protected override string BuildDataSourceInfoFromDbInfo(List<DbTableDefinition> info)
        {
            var dataSourceInfo = string.Empty;
            foreach (var e in info)
            {
                dataSourceInfo += $"{e.Name}\n";
            }
            return dataSourceInfo;
        }

        protected override ChatMessage BuildTableSelectionMessage(string dbType, string currentSqlMessage)
            => new ChatMessage(ChatRole.System, $@"
あなたはSQLの専門家です。
ユーザーが求めるSQLを書くことが目的です。
ユーザーの使っているDBの種類は
{dbType} です。

{currentSqlMessage}

まずSQLで使うテーブルを決めます。決め方の優先順:
1. ユーザーがチャットで使うテーブルを指定・示唆した場合はそれに従う(最優先)
2. ユーザーの要求から適切なテーブルをあなたが判断できる場合は、確認を待たずに選択してよい
3. 判断に迷う場合(候補が複数ある・情報が足りない)だけ、候補を挙げてユーザーに質問して選んでもらう

使うテーブルが決まったら、以下の囲いの中にテーブル名をカンマ区切りで出力してください。

```tbl

```

この出力があると各テーブルの列定義が提供されるので、それを見てSQLを書きます。
列定義の提供前にSQLを書かないでください(列名の推測は厳禁です)。
後からテーブルを追加したくなった場合は、再度 ```tbl``` の囲いで追加分を含めて出力してください。
{BuildConfidenceGateSection()}{BuildModuleMappingSection()}
");

        protected override ChatMessage BuildDataSourceInfoMessage(string dataSourceInfo)
            => new ChatMessage(ChatRole.System, $@"
テーブル情報です。
{dataSourceInfo}
");

        protected override ChatMessage BuildSqlWritingMessage()
            => new ChatMessage(ChatRole.System, $@"
今からはSQLを書くフェーズです。
引き続き、ユーザーと話しつつクエリの設計を進めてください。
クエリを作る以外にも質問があれば答えてください。
SQLの部分は以下のように囲ってください。

```sql

```
このSQLはC#からDbConnectionを使って実行します。DECLAREなどその方法で使えないSQLは入れないでください。パラメータをユーザーに求められた場合はその前提で書いてください。
またパラメータは指定しない場合はその条件を無視するようなSQLにしてください。
例えば以下のようなものです。
saledate >= @p1 OR @p1 IS NULL

最終的にSelectされる項目とパラメータは以下の形でも出力してください。
カラムはIsParameterを出力しないでください。
パラメータはIsParameterをtrueにしてNameは@や:などを含めた形で書いてください。
```schema
[
  {{
    ""Name"": ""name"",
    ""DbType"": ""db raw type""
  }},
  {{
    ""IsParameter"": true,
    ""Name"": ""@name"",
    ""DbType"": ""db raw type""
  }}
]
```

C#の解説は必要ありません。

たまにあなたが作ったSQLを実行すると失敗するときがあります。
その場合はユーザーはあなたにその旨を伝え作り直しを要求します。
その場合でも言い訳は書かなくていいので簡潔にシンプルに迅速にSQLとスキーマを返してください。

与えられたテーブル情報に無いテーブルが必要になった場合や、ユーザーが別のテーブルを指定した場合は、
再度 ```tbl``` の囲いで必要なテーブル名(追加分を含む)を出力してください。列定義が提供されます。
定義の無いテーブルの列名を推測で書くのは厳禁です。
{BuildConfidenceGateSection()}{BuildModuleMappingSection()}
");

        protected override ChatMessage BuildSelectedTableInfoMessage(List<DbTableDefinition> info, List<string> selectedTables)
            => new ChatMessage(ChatRole.System, $@"
現在選択されているテーブルの列定義です。ここに無いテーブルの列名を推測で使わないでください。
他のテーブルが必要になったら、再度 ```tbl``` の囲いでテーブル名を出力すれば列定義が提供されます。
{CreateDbInfo(info, selectedTables)}
");

        // 検証時のバインド値。通常のパラメータは NULL でよい(「@p IS NULL で無視」前提の書き方)が、
        // LIMIT/OFFSET に使われるシステムページングパラメータは NULL だと SQLite 等で
        // datatype mismatch になるため整数を入れる。
        static object GetValidationBindValue(string name)
        {
            var bare = name.TrimStart('@', ':');
            if (bare.Equals(DbParameterSetting.ROWS_PER_PAGE_PARA, StringComparison.OrdinalIgnoreCase)) return 1;
            if (bare.Equals(DbParameterSetting.OFFSET, StringComparison.OrdinalIgnoreCase)) return 0;
            return DBNull.Value;
        }

        // SELECT を実DBでスキーマ取得のみ実行して検証する(行は読まないので副作用・重い読み取りは発生しない)。
        // 接続できない環境(検証環境なし)では黙ってスキップし、従来どおり適用だけ行う。
        protected override async Task<(bool Ran, string? Error)> ValidateSqlAsync(string sql, List<DbParameterSetting> dbParams)
        {
            IDbAccessor accessor;
            try
            {
                accessor = _editor.CreateDbAccessor();
            }
            catch
            {
                return (false, null);
            }

            try
            {
                System.Data.Common.DbConnection conn;
                try
                {
                    conn = accessor.GetConnection(_dataSourceName);
                    if (conn.State != ConnectionState.Open) await conn.OpenAsync();
                }
                catch
                {
                    return (false, null); //DBに接続できない環境では検証しない
                }

                try
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    foreach (var name in CollectBindParameterNames(sql, dbParams))
                    {
                        var p = cmd.CreateParameter();
                        //Oracle の ParameterName は接頭辞なしが慣例。他プロバイダは @ 付きのままで良い
                        p.ParameterName = name.StartsWith(":") ? name.Substring(1) : name;
                        p.Value = GetValidationBindValue(name);
                        cmd.Parameters.Add(p);
                    }
                    await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly);
                    return (true, null);
                }
                catch (Exception e)
                {
                    return (true, e.Message);
                }
            }
            finally
            {
                await accessor.DisposeAsync();
            }
        }
    }
}

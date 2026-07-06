using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Extensions.AI;
using System.Text;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat
{
    /// <summary>
    /// モジュール設定(フィールド定義)と現在のDBスキーマの差分から、DBを設計に合わせるDDLを
    /// AIで生成する。機械的な <see cref="DbMapping.CreateDDL"/> を「安全な型のデフォルト」として渡し、
    /// それを超える内容(フィールド制約に基づく型最適化・型変更ALTER・インデックス等)をAIに作らせる。
    /// 生成したDDLは実行せず、呼び出し側がモードレスの DDLWindow に表示してユーザーにRunさせる。
    /// </summary>
    internal class ModuleDdlGenerator
    {
        readonly IChatClient _chatClient;

        public ModuleDdlGenerator(Func<IChatClient> createChatClient) => _chatClient = createChatClient();

        public async Task<string> GenerateAsync(ModuleDesign module, DataSourceType dataSourceType,
            List<DbTableDefinition> existingTables, List<string> mechanicalBaseline, string userInstruction)
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, SystemPrompt),
                new ChatMessage(ChatRole.User, BuildContext(module, dataSourceType, existingTables, mechanicalBaseline, userInstruction)),
            };

            var result = await _chatClient.GetResponseAsync(messages);
            var text = result.Text;
            return ExtractSql(text);
        }

        static string BuildContext(ModuleDesign module, DataSourceType dataSourceType,
            List<DbTableDefinition> existingTables, List<string> mechanicalBaseline, string userInstruction)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"DB種別: {dataSourceType}");
            sb.AppendLine($"対象テーブル: {module.DbTable}");
            sb.AppendLine();

            sb.AppendLine("## ★このDB({0})での列の型変更方法（最優先・他の方言の構文は絶対に使わない）".Replace("{0}", dataSourceType.ToString()));
            sb.AppendLine(TypeChangeGuidance(dataSourceType, module.DbTable));
            sb.AppendLine();

            sb.AppendLine("## 現在のモジュール定義(フィールド)");
            sb.AppendLine("各フィールドの型(TypeFullName)と DbColumn、MaxLength / Max / MaxFractionDigits / IsRequired 等の制約から、最適な列型を判断してください。");
            sb.AppendLine(JsonConverterEx.SerializeObject(module.Fields));
            sb.AppendLine();

            sb.AppendLine("## 現在のDBスキーマ(対象テーブルの実際の列)");
            var existing = existingTables.FirstOrDefault(
                t => string.Equals(t.Name, module.DbTable, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                sb.AppendLine("テーブルは存在しません(新規作成が必要)。");
            }
            else
            {
                foreach (var c in existing.Columns)
                    sb.AppendLine($"- {c.Name}: {c.RawDbTypeName} (.NET型 {c.NetTypeFullName}, {(c.IsNullable ? "NULL可" : "NOT NULL")})");
            }
            sb.AppendLine();

            sb.AppendLine("## 機械生成の基準DDL(型の安全なデフォルト。列名と対応は尊重しつつ、型はここから改善してよい)");
            sb.AppendLine("※この中の /* ... */ コメントブロック(作り直し用 DROP+CREATE / 列削除候補)は参考です。ツールが自動で付けるので、あなたは出力しないこと。あなたは ADD/CREATE/インデックス等の実行部分だけを出す。");
            sb.AppendLine(string.Join(Environment.NewLine, mechanicalBaseline));
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(userInstruction))
            {
                sb.AppendLine("## ユーザーの指示(インデックス等の意図の参考)");
                sb.AppendLine(userInstruction);
            }

            return sb.ToString();
        }

        // DB種別はコード側で確定しているので、その方言の型変更方法だけを渡す(5方言を並べて選ばせると誤った方言を引く)。
        static string TypeChangeGuidance(DataSourceType dataSourceType, string table) => dataSourceType switch
        {
            DataSourceType.PostgreSQL =>
                $"ALTER TABLE {table} ALTER COLUMN <列> TYPE <新型> USING <列>::<新型>; の形で変更します。",
            DataSourceType.MySQL =>
                $"ALTER TABLE {table} MODIFY COLUMN <列> <新型>; の形で変更します。",
            DataSourceType.SQLServer =>
                $"ALTER TABLE {table} ALTER COLUMN <列> <新型>; の形で変更します(USING は付けない)。",
            DataSourceType.Oracle =>
                $"ALTER TABLE {table} MODIFY (<列> <新型>); の形で変更します。",
            DataSourceType.SQLite =>
                $@"SQLite には列の型を変更する構文がありません。`ALTER TABLE ... ALTER COLUMN ... TYPE ...` は絶対に使わないでください(構文エラーになります)。
**この作り直し手順は『列名が一致する既存列の型を変える必要があるとき』だけ使います。単に列の顔ぶれ(名前)が違うだけなら作り直さず、不足列は ALTER TABLE ADD、不明な既存列はコメントのDROP候補にとどめてください。**
型を変えるにはテーブルを作り直してデータを移します。手順:
  1. 新スキーマで一時テーブルを作る(例 {table}__new)。型は最適化後の型にする。CREATE文は基準DDLの CREATE TABLE をベースに型だけ直して作る。
  2. INSERT INTO {table}__new (列...) SELECT 列... FROM {table};  -- **移すのは新旧で名前が一致する列だけ**。型が変わる列は CAST(<列> AS <新型>) で移す。新規に増える列は SELECT に含めない(既定値/NULLになる)。名前が違う列を推測で対応付けない。
  3. DROP TABLE {table};
  4. ALTER TABLE {table}__new RENAME TO {table};",
            _ => "DBの標準的な方法で列の型を変更してください。"
        };

        static string ExtractSql(string text)
        {
            var sql = Regex.Match(text, @"```sql\s(.*?)\s```", RegexOptions.Singleline);
            if (sql.Success) return sql.Groups[1].Value.Trim();

            var generic = Regex.Match(text, @"```[a-zA-Z]*\s(.*?)\s```", RegexOptions.Singleline);
            if (generic.Success) return generic.Groups[1].Value.Trim();

            return text.Trim();
        }

        const string SystemPrompt = @"
あなたはデータベースのスキーマ移行(DDL)の専門家です。
ローコードフレームワークのモジュール定義(フィールド)と現在のDBスキーマの差分から、
DBを設計に合わせるための実行可能なDDLを生成します。

## あなたが出力するもの / 出力しないもの
- あなたが出力するのは「**実際に実行して設計に合わせる部分**」だけです: 具体的には ①テーブルが無ければ CREATE TABLE、②既存テーブルに不足している列の ALTER TABLE ADD、③同名列の型変更(必要時)、④インデックス、⑤ユーザーが明示的に頼んだ削除の実行DROP。
- **次のものは絶対に出力しないでください(ツールが決定的な参考コメントとして自動で付けます)**: 「テーブル作り直し用の DROP TABLE + CREATE TABLE のコメントブロック」「頼まれていない列の DROP COLUMN 候補」。あなたがこれらを重複して出すと二重になります。基準DDLの中にこれらのコメントブロックが含まれていても、それは無視して(まねして出さないで)ください。

## 出力形式
- 出力は単一の ```sql ブロックの中に、実行可能なDDLだけを入れてください。C#やDDL以外の解説は不要です。
- このSQLはC#から DbConnection で実行されます。DECLARE 等そのままでは実行できない構文は入れないでください。
- 変更が一切不要な場合(不足列も型変更もインデックスも削除要求も無い)は ```sql の中に `-- 変更は不要です` だけを出力してください。

## 型の決定(ここが機械生成を超える肝)
- フレームワークはDB列の.NET型から値を変換して読み書きします。値を保持でき、変換可能で、サイズが足りる型であれば自由に選べます。
- 文字列(TextField): MaxLength があれば VARCHAR/NVARCHAR(MaxLength) 等の上限付き、無ければ可変長最大(TEXT 等)。常に最大長にしないこと。
- 数値(NumberField): MaxFractionDigits があれば DECIMAL(p, s)、無ければ INTEGER 系。Max から桁を見積もってよい。
- 基準DDLの列名・対応関係は尊重し、型だけを制約から最適化してください。
- システムフィールド(Id 等)の主キー・自動採番、他テーブルを参照する Id 列の整数幅は基準DDLに従ってください(食い違うとFKや桁で破綻します)。

## 列の対応(最重要・データ保護。ここを間違えるとデータが壊れます)
- モジュールのフィールドと既存DB列の対応は、**列名(DbColumn)が完全一致するもの同士だけ**です。名前が違う列を「リネームされた同じ列」「同じ意味の列」と**推測してはいけません**。例: DBの `zip_code` とフィールドの `postal_code`、DBの `address` とフィールドの `address1` を同一視して移し替えるのは**禁止**(推測のマッピングはデータを壊します)。
- モジュールにあってDBに無い列(名前が一致しない) → **新しい列**として `ALTER TABLE ADD` で追加する。既存の別名列に紐付けたり値を移したりしない。
- DBにあってモジュールに無い列(名前が一致しない) → **勝手に消さない**。念のための削除候補として `DROP COLUMN` を**コメント(-- または /* */)で**出すだけにとどめ、実行形にはしない。
- **`DROP TABLE` / テーブルの作り直し / `INSERT ... SELECT` による列の移し替えは、次のいずれかのときだけ**行ってよい:
  (a) SQLite で「**列名が一致する既存列**の型を変える」必要があり、他に方法が無いとき(この場合も移し替えは**同名列のみ**、新規列は SELECT に含めない)。
  (b) ユーザーが「テーブルを作り直して」「○○列を△△にリネームして」等、**明示的に**作り直し/リネームを指示したとき。
  それ以外で、**単に列名が食い違うというだけでテーブルを作り直してはいけません**。既定は「不足列を ADD ・不明な既存列はコメントのDROP候補」です。

## 差分の作り方
- テーブルが存在しなければ CREATE TABLE。存在すれば、上記「列の対応」に従い、不足している列だけ `ALTER TABLE ADD`。
- **同名の**既存列の型とフィールドの型が食い違う場合は、その列を新しい型に変更してください。**変更構文はメッセージ冒頭の「★このDBでの列の型変更方法」に必ず従い、他の方言の構文は絶対に使わないこと**(構文エラーになります)。名前が違う列は「型が違う」ではなく「別の列」です(型変更の対象にしない)。
- 型変更でデータ変換が失敗しうる場合(文字列→数値 等)も、最終的にユーザーが内容を確認して実行するので、移行DDLとして出力してください。

## インデックス
- 検索やFKに使う列にはインデックスを提案してください。具体的には、他テーブルを参照する Id 列・LinkField の列、ユーザーが「検索する」と言った列、コード等の一意な列(UNIQUE)。
- 既存インデックスの情報は渡されません。CREATE INDEX IF NOT EXISTS が使えるDB(PostgreSQL/SQLite/MySQL)では IF NOT EXISTS を付けてください。SQL Server / Oracle は重複作成しないよう、ユーザーが確認して実行する前提でそのまま出してください。

## 安全規則(重要)
- **頼まれていない破壊操作(DROP TABLE / 孤立した既存列の DROP COLUMN / 桁やサイズを縮小する型変更)は、コメントでも実行形でも出力しないでください。** それらの候補はツールが参考コメントとして別に付けます。あなたは追加・拡張(と、明示要求された削除)だけを出します。
- **ユーザーが削除/縮小を明示的に求めている場合は、その操作を実行可能な形(コメントしない)で出力してください。** 例: 「test カラムを削除して」「このテーブルを削除して」と言われたら、その `DROP COLUMN test;` / `DROP TABLE ...;` は**コメントせず実行可能**に出す(ユーザーが意図した操作なので)。
";
    }
}

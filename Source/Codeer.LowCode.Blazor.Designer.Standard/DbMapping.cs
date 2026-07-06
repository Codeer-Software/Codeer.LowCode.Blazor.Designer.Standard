using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using System.Reflection;

namespace Codeer.LowCode.Blazor.Designer.Standard
{
    /// <summary>
    /// モジュールデザインから機械的に DDL を生成する (AI は使わない決定的な生成)。
    /// デザイナアプリのメニュー (Create DDL 等) からも使う公開 API。
    /// </summary>
    public static class DbMapping
    {
        /// <summary>
        /// モジュールの DDL を生成する。existingTables (現在のDBスキーマ) を渡すと差分DDLになる:
        /// テーブルが無い/スキーマ未取得 → CREATE TABLE、テーブルが有る → 不足している列だけ ALTER TABLE ADD。
        /// 追加のみ (型変更・列削除はしない=安全側)。
        /// </summary>
        public static List<string> CreateDDL(this ModuleDesign module, DataSourceType dataSourceType,
            List<DbTableDefinition>? existingTables = null)
        {
            var columns = module.Fields
                .SelectMany(field => CreateColumns(dataSourceType, field))
                .ToList();

            var existing = existingTables?.FirstOrDefault(
                t => string.Equals(t.Name, module.DbTable, StringComparison.OrdinalIgnoreCase));

            // テーブルが無い(または接続できずスキーマ未取得)→ 従来どおりフル CREATE。
            if (existing == null) return CreateTableDdl(module.DbTable, columns);

            // テーブルが有る → 作り直し用 DROP+CREATE / 列削除候補の参考コメントを先に出し、
            // その下に「設計にあって DB に無い列」の ALTER ADD を出す。ALTER が無ければメッセージ。
            var ddl = ReferenceComments(module.DbTable, existing, columns);

            var existingColumns = new HashSet<string>(
                existing.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            var missing = columns.Where(c => !existingColumns.Contains(c.Name)).ToList();

            if (missing.Count == 0)
                ddl.Add($"-- {module.DbTable}: 追加する列はありません");
            else
                ddl.AddRange(missing.Select(c => AlterAddColumn(dataSourceType, module.DbTable, c.Name, c.Type)));

            return ddl;
        }

        // 既存テーブル向けの「参考(安全な足場)コメント」= ①作り直し用 DROP+CREATE ②列削除候補 DROP COLUMN。
        // 実行されないコメントブロックとして、DB更新DDLの先頭に決定的に(毎回同じ形で)付ける。
        // テーブルが無い(=新規作成)ときは足場は不要なので空を返す。
        internal static List<string> CreateDdlReferenceComments(this ModuleDesign module, DataSourceType dataSourceType,
            List<DbTableDefinition>? existingTables = null)
        {
            var existing = existingTables?.FirstOrDefault(
                t => string.Equals(t.Name, module.DbTable, StringComparison.OrdinalIgnoreCase));
            if (existing == null) return new();

            var columns = module.Fields
                .SelectMany(field => CreateColumns(dataSourceType, field))
                .ToList();
            return ReferenceComments(module.DbTable, existing, columns);
        }

        static List<string> ReferenceComments(string table, DbTableDefinition existing, List<(string Name, string Type)> columns)
        {
            // ① 作り直し用は「ブロックコメント」で DROP TABLE + CREATE TABLE をまとめて出す(必要ならコメントを外して使う。データは消える)。
            var ddl = new List<string> { "/* テーブルを作り直す場合(既存データは消えます):" };
            ddl.Add($"DROP TABLE {table};");
            ddl.AddRange(CreateTableDdl(table, columns));
            ddl.Add("*/");
            ddl.Add("");

            // ② 列削除は「行コメント」で現在の全カラム分を1行ずつ出す(消したい列の行だけ -- を外して使う)。
            if (existing.Columns.Count > 0)
            {
                ddl.Add("-- 不要な列を削除する場合は、対象行の -- を外してください:");
                ddl.AddRange(existing.Columns.Select(c => $"-- ALTER TABLE {table} DROP COLUMN {c.Name};"));
                ddl.Add("");
            }
            return ddl;
        }

        static List<string> CreateTableDdl(string table, List<(string Name, string Type)> columns)
        {
            var ddl = new List<string> { $"CREATE TABLE {table} (" };
            for (var i = 0; i < columns.Count; i++)
            {
                var comma = i < columns.Count - 1 ? "," : "";
                ddl.Add($"  {columns[i].Name} {columns[i].Type}{comma}");
            }
            ddl.Add(");");
            return ddl;
        }

        static string AlterAddColumn(DataSourceType dataSourceType, string table, string name, string type) => dataSourceType switch
        {
            DataSourceType.SQLServer => $"ALTER TABLE {table} ADD {name} {type};",
            DataSourceType.Oracle => $"ALTER TABLE {table} ADD ({name} {type});",
            _ => $"ALTER TABLE {table} ADD COLUMN {name} {type};" // SQLite / PostgreSQL / MySQL
        };

        // フィールド → (列名, 型) のペア列。File/PasswordHash のように複数列を持つフィールドは複数返す。
        // 文字列に組み立てず素のペアで返すので、差分生成(ALTER TABLE ADD)でもそのまま使える。
        internal static List<(string Name, string Type)> CreateColumns(DataSourceType dataSourceType, FieldDesignBase field)
        {
            switch (field)
            {
                case DbValueFieldDesignBase dbValue when !string.IsNullOrEmpty(dbValue.DbColumn):
                    return [(dbValue.DbColumn, ColumnType(dataSourceType, dbValue))];

                // 楽観ロックは DbValueFieldDesignBase ではないが DbColumn を持つ。バージョン整数列として出す
                // (IncrementVersion 運用。PostgreSQL の xmin のようなネイティブ行バージョンは DDL 対象外)。
                case OptimisticLockingFieldDesign opt when !string.IsNullOrEmpty(opt.DbColumn):
                    return [(opt.DbColumn, IntegerType(dataSourceType))];

                case FileFieldDesign file:
                {
                    var (guid, name, size) = FileColumnTypes(dataSourceType);
                    var list = new List<(string, string)>();
                    if (!string.IsNullOrEmpty(file.DbColumnFileGuid)) list.Add((file.DbColumnFileGuid, guid));
                    if (!string.IsNullOrEmpty(file.DbColumnFileName)) list.Add((file.DbColumnFileName, name));
                    if (!string.IsNullOrEmpty(file.DbColumnFileSize)) list.Add((file.DbColumnFileSize, size));
                    return list;
                }

                default:
                {
                    // 特定のフィールド型を名指しせず、[DbColumn] を付けた文字列プロパティを汎用に列挙する。
                    // 複数列を持つ拡張フィールド(例: PasswordHash の Hash/Salt)や将来のフィールドを、
                    // このアセンブリが型を知らなくても扱える(既定の列型は text)。
                    var text = TextType(dataSourceType);
                    var list = new List<(string, string)>();
                    foreach (var prop in field.GetType().GetProperties())
                    {
                        if (prop.PropertyType != typeof(string)) continue;
                        if (prop.GetCustomAttribute<DbColumnAttribute>() == null) continue;
                        var column = prop.GetValue(field) as string;
                        if (!string.IsNullOrEmpty(column)) list.Add((column, text));
                    }
                    return list;
                }
            }
        }

        static string ColumnType(DataSourceType dataSourceType, DbValueFieldDesignBase field)
        {
            var type = BaseColumnType(dataSourceType, field);

            // 必須項目は NOT NULL。Id は PK 定義に含まれる(暗黙 NOT NULL)ので付けない。
            if (field is not IdFieldDesign && field.IsRequired)
                type += " NOT NULL";

            return type;
        }

        static string BaseColumnType(DataSourceType dataSourceType, DbValueFieldDesignBase field)
        {
            // 小数桁を持つ数値は整数固定だと小数が落ちるため DECIMAL 系にする。
            if (field is NumberFieldDesign number && number.MaxFractionDigits is > 0)
                return DecimalType(dataSourceType, number.MaxFractionDigits.Value);

            var fieldType = field.GetType();

            // "id" 以外の名前の IdField は外部キー用途。参照先 PK と同じ整数幅にする
            // (NumberField の INT にすると PK が BIGINT のとき幅が食い違い、FK が張れず桁溢れもする)。
            if (fieldType == typeof(IdFieldDesign) && field.DbColumn.ToLower() != "id")
                return ForeignKeyIdType(dataSourceType);

            // 辞書に無い型(拡張ライブラリ製フィールド等)は例外にせずテキストにフォールバックする。
            return TypeMapping(dataSourceType).TryGetValue(fieldType, out var columnType)
                ? columnType
                : TextType(dataSourceType);
        }

        static string IntegerType(DataSourceType dataSourceType) => TypeMapping(dataSourceType)[typeof(NumberFieldDesign)];

        // 外部キー用 Id 列の型。参照先 PK(自動採番)と同じ整数幅に合わせる。
        static string ForeignKeyIdType(DataSourceType dataSourceType) => dataSourceType switch
        {
            DataSourceType.SQLite => "INTEGER",
            DataSourceType.SQLServer => "BIGINT",
            DataSourceType.PostgreSQL => "BIGINT",
            DataSourceType.MySQL => "BIGINT",
            DataSourceType.Oracle => "NUMBER",
            _ => throw new Exception($"Database type not supported: {dataSourceType}")
        };

        static string TextType(DataSourceType dataSourceType) => TypeMapping(dataSourceType)[typeof(TextFieldDesign)];

        static string DecimalType(DataSourceType dataSourceType, int scale) => dataSourceType switch
        {
            DataSourceType.SQLite => "NUMERIC",
            DataSourceType.SQLServer => $"DECIMAL(18,{scale})",
            DataSourceType.PostgreSQL => $"NUMERIC(18,{scale})",
            DataSourceType.MySQL => $"DECIMAL(18,{scale})",
            DataSourceType.Oracle => $"NUMBER(18,{scale})",
            _ => throw new Exception($"Database type not supported: {dataSourceType}")
        };

        static (string Guid, string Name, string Size) FileColumnTypes(DataSourceType dataSourceType) => dataSourceType switch
        {
            DataSourceType.SQLite => ("TEXT", "TEXT", "INTEGER"),
            DataSourceType.SQLServer => ("UNIQUEIDENTIFIER", "NVARCHAR(MAX)", "INT"),
            DataSourceType.PostgreSQL => ("UUID", "TEXT", "INTEGER"),
            DataSourceType.MySQL => ("CHAR(36)", "TEXT", "INT"),
            DataSourceType.Oracle => ("VARCHAR2(36)", "VARCHAR2(4000)", "NUMBER"),
            _ => throw new Exception($"Database type not supported: {dataSourceType}")
        };

        static Dictionary<Type, string> TypeMapping(DataSourceType dataSourceType) => dataSourceType switch
        {
            DataSourceType.SQLite => SqliteTypeMapping,
            DataSourceType.SQLServer => SqlserverTypeMapping,
            DataSourceType.PostgreSQL => PostgresqlTypeMapping,
            DataSourceType.MySQL => MysqlTypeMapping,
            DataSourceType.Oracle => OracleTypeMapping,
            _ => throw new Exception($"Database type not supported: {dataSourceType}")
        };

        private static readonly Dictionary<Type, string> SqliteTypeMapping = new()
        {
            {typeof(IdFieldDesign), "INTEGER PRIMARY KEY AUTOINCREMENT"},
            {typeof(TextFieldDesign), "TEXT"},
            {typeof(NumberFieldDesign), "INTEGER"},
            {typeof(DateFieldDesign), "DATE"},
            {typeof(DateTimeFieldDesign), "DATETIME"},
            {typeof(TimeFieldDesign), "TIME"},
            {typeof(BooleanFieldDesign), "BOOLEAN"},
            {typeof(LinkFieldDesign), "TEXT"},
            {typeof(SelectFieldDesign), "TEXT"},
            {typeof(RadioGroupFieldDesign), "TEXT"}
        };

        private static readonly Dictionary<Type, string> SqlserverTypeMapping = new()
        {
            {typeof(IdFieldDesign), "BIGINT IDENTITY(1,1) PRIMARY KEY"},
            {typeof(TextFieldDesign), "NVARCHAR(MAX)"},
            {typeof(NumberFieldDesign), "INT"},
            {typeof(DateFieldDesign), "DATE"},
            {typeof(DateTimeFieldDesign), "DATETIME"},
            {typeof(TimeFieldDesign), "TIME"},
            {typeof(BooleanFieldDesign), "BIT"},
            {typeof(LinkFieldDesign), "NVARCHAR(MAX)"},
            {typeof(SelectFieldDesign), "NVARCHAR(MAX)"},
            {typeof(RadioGroupFieldDesign), "NVARCHAR(MAX)"}
        };

        private static readonly Dictionary<Type, string> PostgresqlTypeMapping = new()
        {
            {typeof(IdFieldDesign), "BIGSERIAL PRIMARY KEY"},
            {typeof(TextFieldDesign), "TEXT"},
            {typeof(NumberFieldDesign), "INTEGER"},
            {typeof(DateFieldDesign), "DATE"},
            {typeof(DateTimeFieldDesign), "TIMESTAMP"},
            {typeof(TimeFieldDesign), "TIME"},
            {typeof(BooleanFieldDesign), "BOOLEAN"},
            {typeof(LinkFieldDesign), "TEXT"},
            {typeof(SelectFieldDesign), "TEXT"},
            {typeof(RadioGroupFieldDesign), "TEXT"}
        };

        private static readonly Dictionary<Type, string> MysqlTypeMapping = new()
        {
            {typeof(IdFieldDesign), "BIGINT AUTO_INCREMENT PRIMARY KEY"},
            {typeof(TextFieldDesign), "TEXT"},
            {typeof(NumberFieldDesign), "INT"},
            {typeof(DateFieldDesign), "DATE"},
            {typeof(DateTimeFieldDesign), "DATETIME"},
            {typeof(TimeFieldDesign), "TIME"},
            {typeof(BooleanFieldDesign), "TINYINT(1)"},
            {typeof(LinkFieldDesign), "TEXT"},
            {typeof(SelectFieldDesign), "TEXT"},
            {typeof(RadioGroupFieldDesign), "TEXT"}
        };

        private static readonly Dictionary<Type, string> OracleTypeMapping = new()
        {
            {typeof(IdFieldDesign), "NUMBER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY"},
            {typeof(TextFieldDesign), "VARCHAR2(4000)"},
            {typeof(NumberFieldDesign), "NUMBER"},
            {typeof(DateFieldDesign), "DATE"},
            {typeof(DateTimeFieldDesign), "TIMESTAMP"},
            {typeof(TimeFieldDesign), "TIMESTAMP WITH LOCAL TIME ZONE"},
            {typeof(BooleanFieldDesign), "NUMBER(1)"},
            {typeof(LinkFieldDesign), "VARCHAR2(4000)"},
            {typeof(SelectFieldDesign), "VARCHAR2(4000)"},
            {typeof(RadioGroupFieldDesign), "VARCHAR2(4000)"}
        };
    }
}

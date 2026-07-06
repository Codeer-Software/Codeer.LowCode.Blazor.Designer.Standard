using System.IO;
using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Microsoft.Data.Sqlite;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// テスト用の使い捨て SQLite DB。AI が生成した DDL を実際に実行して通るか・スキーマが期待どおりかを検証する。
    /// </summary>
    public sealed class SqliteTestDb : IDisposable
    {
        public string Path { get; }
        public string ConnectionString { get; }

        public SqliteTestDb(string fileName)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clb_chat_test", fileName);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            if (File.Exists(Path)) { ReleaseAndDelete(); }
            ConnectionString = $"Data Source={Path}";
            // ファイルを作る
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
        }

        /// <summary>DDL(複数文・コメント可)を実行する。失敗時は例外。</summary>
        public void ExecuteDdl(string sql)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        /// <summary>任意SQL実行(INSERT/SELECT等の検証用)。</summary>
        public long ExecuteScalarLong(string sql)
        {
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var o = cmd.ExecuteScalar();
            return o == null || o is DBNull ? 0 : Convert.ToInt64(o);
        }

        /// <summary>現在の全テーブル定義を読む(DbInfo 用)。PRAGMA table_info ベース。</summary>
        public List<DbTableDefinition> GetSchema()
        {
            var result = new List<DbTableDefinition>();
            using var conn = new SqliteConnection(ConnectionString);
            conn.Open();

            var tableNames = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
                using var r = cmd.ExecuteReader();
                while (r.Read()) tableNames.Add(r.GetString(0));
            }

            foreach (var table in tableNames)
            {
                var def = new DbTableDefinition { Name = table };
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info('{table}')";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var name = r.GetString(1);          // name
                    var rawType = r.IsDBNull(2) ? "" : r.GetString(2); // type
                    var notnull = r.GetInt32(3) == 1;   // notnull
                    def.Columns.Add(new DbColumnDefinition
                    {
                        Name = name,
                        RawDbTypeName = rawType,
                        NetTypeFullName = MapNetType(rawType),
                        IsNullable = !notnull
                    });
                }
                result.Add(def);
            }
            return result;
        }

        /// <summary>NOT NULL 制約に引っかからないよう全列にダミー値(指定があれば上書き)を入れて1行INSERTする。</summary>
        public void InsertRow(string table, Dictionary<string, object?> overrides)
        {
            var schema = GetSchema().FirstOrDefault(t => string.Equals(t.Name, table, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"table {table} not found");
            var lookup = new Dictionary<string, object?>(overrides, StringComparer.OrdinalIgnoreCase);
            var cols = new List<string>();
            var vals = new List<string>();
            foreach (var c in schema.Columns)
            {
                cols.Add(c.Name);
                var v = lookup.TryGetValue(c.Name, out var ov) ? ov : DummyFor(c.RawDbTypeName);
                vals.Add(Literal(v));
            }
            ExecuteDdl($"INSERT INTO {table} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", vals)});");
        }

        static object DummyFor(string rawType)
        {
            var t = rawType.ToUpperInvariant();
            if (t.Contains("INT")) return 0;
            if (t.Contains("REAL") || t.Contains("NUMERIC") || t.Contains("DECIMAL") || t.Contains("FLOA") || t.Contains("DOUB")) return 0;
            if (t.Contains("BOOL")) return 0;
            if (t.Contains("DATETIME") || t.Contains("TIMESTAMP")) return "2020-01-01 00:00:00";
            if (t.Contains("DATE")) return "2020-01-01";
            if (t.Contains("TIME")) return "00:00:00";
            return "x";
        }

        static string Literal(object? v)
            => v == null ? "NULL"
             : v is string s ? "'" + s.Replace("'", "''") + "'"
             : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!;

        public List<string> GetColumnNames(string table)
            => GetSchema().FirstOrDefault(t => string.Equals(t.Name, table, StringComparison.OrdinalIgnoreCase))?
                .Columns.Select(c => c.Name).ToList() ?? new();

        static string MapNetType(string rawType)
        {
            var t = rawType.ToUpperInvariant();
            if (t.Contains("INT")) return typeof(long).FullName!;
            if (t.Contains("CHAR") || t.Contains("TEXT") || t.Contains("CLOB")) return typeof(string).FullName!;
            if (t.Contains("REAL") || t.Contains("FLOA") || t.Contains("DOUB") || t.Contains("NUMERIC") || t.Contains("DECIMAL"))
                return typeof(decimal).FullName!;
            if (t.Contains("BOOL")) return typeof(bool).FullName!;
            if (t.Contains("DATETIME") || t.Contains("TIMESTAMP")) return typeof(DateTime).FullName!;
            if (t.Contains("DATE")) return typeof(DateTime).FullName!;
            if (t.Contains("TIME")) return typeof(TimeSpan).FullName!;
            return typeof(string).FullName!;
        }

        void ReleaseAndDelete()
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(Path)) File.Delete(Path); } catch { /* 残ってもテンプ配下なので無視 */ }
        }

        public void Dispose() => ReleaseAndDelete();
    }
}

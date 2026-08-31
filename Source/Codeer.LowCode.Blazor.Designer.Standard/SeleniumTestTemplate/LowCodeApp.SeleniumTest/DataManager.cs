using System.Data;
using Codeer.LowCode.Blazor.DbAccess;
using Dapper;

namespace LowCodeApp.SeleniumTest;

/// <summary>
/// テストデータの準備・確認。アプリと同じ DB アクセス層 (Codeer.LowCode.Blazor.DbAccess) を使うので、
/// SQLite / PostgreSQL / SQL Server / MySQL / Oracle のどれでも同じコードで動く。
/// 接続先は testsettings(.local).json の DataSources / ConnectionStrings。
///
/// 使い方 (シナリオの [SetUp] で、そのテストが触るテーブルだけを初期化する):
/// <code>
///   DataManager.DeleteAll("SampleSQLite", "order_details", "orders");   // 子 → 親の順
///   var id = DataManager.ExecuteScalar&lt;long&gt;("SampleSQLite",
///       "INSERT INTO orders (customer, total) VALUES (@c, @t); SELECT last_insert_rowid();", new { c = "ACME", t = 100 });
/// </code>
/// テーブル・列名はデザイン (Modules/*.mod.json の TableName / DbColumn) と一致させる。
/// 他のテストと同じテーブルを使うテストは並列実行しない (NUnit の既定は直列)。
/// </summary>
public static class DataManager
{
    /// <summary>データソース名 (designer.settings.json / testsettings.json の DataSources[].Name) の接続を開く。using で閉じる。</summary>
    public static ConnectionScope Open(string dataSourceName)
        => new(dataSourceName);

    public static int Execute(string dataSourceName, string sql, object? param = null)
    {
        using var scope = Open(dataSourceName);
        return scope.Connection.Execute(sql, param);
    }

    public static T ExecuteScalar<T>(string dataSourceName, string sql, object? param = null)
    {
        using var scope = Open(dataSourceName);
        return scope.Connection.ExecuteScalar<T>(sql, param)!;
    }

    public static List<T> Query<T>(string dataSourceName, string sql, object? param = null)
    {
        using var scope = Open(dataSourceName);
        return scope.Connection.Query<T>(sql, param).ToList();
    }

    public static List<dynamic> Query(string dataSourceName, string sql, object? param = null)
    {
        using var scope = Open(dataSourceName);
        return scope.Connection.Query(sql, param).ToList();
    }

    public static long Count(string dataSourceName, string table, string? where = null, object? param = null)
        => ExecuteScalar<long>(dataSourceName, $"SELECT COUNT(*) FROM {table}" + (string.IsNullOrEmpty(where) ? string.Empty : " WHERE " + where), param);

    /// <summary>指定テーブルの全行を削除する (外部キーがあるので子テーブル → 親テーブルの順に渡す)。</summary>
    public static void DeleteAll(string dataSourceName, params string[] tables)
    {
        using var scope = Open(dataSourceName);
        foreach (var table in tables) scope.Connection.Execute($"DELETE FROM {table}");
    }

    /// <summary>SQL ファイル (DDL / seed) を実行する。文の区切りは ";" 改行 (Oracle の PL/SQL ブロックには使わない)。</summary>
    public static void ExecuteSqlFile(string dataSourceName, string path)
    {
        var text = File.ReadAllText(path);
        using var scope = Open(dataSourceName);
        foreach (var statement in text.Split(";\r\n", StringSplitOptions.RemoveEmptyEntries).SelectMany(s => s.Split(";\n", StringSplitOptions.RemoveEmptyEntries)))
        {
            if (string.IsNullOrWhiteSpace(statement)) continue;
            scope.Connection.Execute(statement);
        }
    }

    /// <summary>DbAccessor の寿命を接続 1 回分に閉じる。</summary>
    public sealed class ConnectionScope : IDisposable
    {
        readonly DbAccessor _accessor;
        public IDbConnection Connection { get; }

        internal ConnectionScope(string dataSourceName)
        {
            var settings = TestSettings.Instance;
            var dataSource = settings.DataSources.FirstOrDefault(d => d.Name == dataSourceName)
                ?? throw new InvalidOperationException($"data source not found in testsettings: {dataSourceName}");
            if (string.IsNullOrEmpty(dataSource.ConnectionString))
                throw new InvalidOperationException($"connection string not set: put ConnectionStrings:{dataSourceName} into testsettings.local.json");
            _accessor = new DbAccessor(settings.DataSources);
            Connection = _accessor.GetConnection(dataSourceName);
        }

        public void Dispose() => _accessor.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

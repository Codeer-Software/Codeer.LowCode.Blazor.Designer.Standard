using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Codeer.LowCode.Blazor.Designer.Standard;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// DB更新DDLの「足場」= 機械生成(DbMapping)の参考コメントブロックが決定的に(毎回同じ形で)出ることを検証する。
    /// この部分は AI に任せず、作り直し用 DROP+CREATE / 列削除候補 DROP COLUMN を必ず・不変で出す(AI非依存の単体テスト)。
    /// </summary>
    [TestFixture]
    public class DdlScaffoldingTest
    {
        static ModuleDesign Module()
        {
            var m = new ModuleDesign { Name = "CustomerMaster", DataSourceName = "Main", DbTable = "customer_master" };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "CustomerCode", DbColumn = "customer_code" });
            m.Fields.Add(new TextFieldDesign { Name = "PostalCode", DbColumn = "postal_code" });
            return m;
        }

        static List<DbTableDefinition> Existing() => new()
        {
            new DbTableDefinition
            {
                Name = "customer_master",
                Columns =
                {
                    new DbColumnDefinition { Name = "id", RawDbTypeName = "INTEGER", IsNullable = false },
                    new DbColumnDefinition { Name = "customer_code", RawDbTypeName = "TEXT", IsNullable = true },
                    new DbColumnDefinition { Name = "zip_code", RawDbTypeName = "TEXT", IsNullable = true },
                    new DbColumnDefinition { Name = "address", RawDbTypeName = "TEXT", IsNullable = true },
                }
            }
        };

        [Test]
        public void 既存テーブルには作り直しはブロックコメント列削除は行コメントで必ず出す()
        {
            var comments = string.Join("\n", Module().CreateDdlReferenceComments(DataSourceType.SQLite, Existing()));

            // ① 作り直し用 DROP+CREATE は「ブロックコメント」/* ... */
            Assert.That(comments, Does.Contain("/* テーブルを作り直す場合"));
            Assert.That(comments, Does.Contain("DROP TABLE customer_master;"));
            Assert.That(comments, Does.Contain("CREATE TABLE customer_master ("));
            Assert.That(comments, Does.Contain("*/"), "作り直しブロックコメントが閉じていない");
            // ② 列削除は「行コメント」-- で現在の全列分
            foreach (var col in new[] { "id", "customer_code", "zip_code", "address" })
                Assert.That(comments, Does.Contain($"-- ALTER TABLE customer_master DROP COLUMN {col};"),
                    $"{col} の DROP COLUMN 行コメントが無い");
        }

        [Test]
        public void 足場は決定的で毎回同じ結果になる()
        {
            var a = Module().CreateDdlReferenceComments(DataSourceType.SQLite, Existing());
            var b = Module().CreateDdlReferenceComments(DataSourceType.SQLite, Existing());
            Assert.That(string.Join("\n", a), Is.EqualTo(string.Join("\n", b)));
        }

        [Test]
        public void テーブルが無いとき足場は空_新規作成はAIのCREATEだけ()
        {
            // 実DBに対象テーブルが無い(existing==null)→ 作り直し/削除候補の足場は不要。
            var comments = Module().CreateDdlReferenceComments(DataSourceType.SQLite, new List<DbTableDefinition>());
            Assert.That(comments, Is.Empty);
        }
    }
}

using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using NUnit.Framework;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// DB列→フィールド変換の契約テスト。
    /// ツールボックスのDB列ドロップとDBインポート(テーブルからモジュール作成)は
    /// DbColumnFieldConverter に一本化されており、変換結果はこのゴールデンで固定する
    /// (かつて2実装が乖離し、DB×型で38差分が生まれた回帰の再発防止)。
    /// </summary>
    [TestFixture]
    public class DbColumnFieldConverterTest
    {
        static FieldDesignBase Convert(DataSourceType type, string rawDbType, string netTypeFullName, bool isNullable = true)
        {
            var dataSource = new DataSource { Name = "Main", DataSourceType = type };
            var col = new DbColumnDefinition
            {
                Name = "col1",
                NetTypeFullName = netTypeFullName,
                RawDbTypeName = rawDbType,
                IsNullable = isNullable,
            };
            var table = new DbTableDefinition { Name = "t", Columns = { col } };
            return DbColumnFieldConverter.Convert(dataSource, table, col);
        }

        static string? DbColumnOf(FieldDesignBase field)
            => field.GetType().GetProperty("DbColumn")?.GetValue(field) as string;

        static void AssertNumber(FieldDesignBase field, int? fractionDigits)
        {
            Assert.That(field, Is.TypeOf<NumberFieldDesign>());
            Assert.That(((NumberFieldDesign)field).MaxFractionDigits, Is.EqualTo(fractionDigits));
            Assert.That(DbColumnOf(field), Is.EqualTo("col1"));
        }

        static void AssertType<T>(FieldDesignBase field) where T : FieldDesignBase
        {
            Assert.That(field, Is.TypeOf<T>());
            Assert.That(DbColumnOf(field), Is.EqualTo("col1"));
        }

        [Test]
        public void 整数列は小数入力不可のNumberField()
        {
            //全DB共通(かつて取込のみ桁0でドロップは桁空白だった差分の固定)
            AssertNumber(Convert(DataSourceType.PostgreSQL, "bigint", typeof(long).FullName!), 0);
            AssertNumber(Convert(DataSourceType.PostgreSQL, "integer", typeof(int).FullName!), 0);
            AssertNumber(Convert(DataSourceType.PostgreSQL, "smallint", typeof(short).FullName!), 0);
            AssertNumber(Convert(DataSourceType.Oracle, "BINARY_INTEGER", typeof(long).FullName!), 0);
            AssertNumber(Convert(DataSourceType.SQLServer, "tinyint", typeof(byte).FullName!), 0);
            AssertNumber(Convert(DataSourceType.MySQL, "year", typeof(int).FullName!), 0);
            AssertNumber(Convert(DataSourceType.SQLite, "integer", typeof(long).FullName!), 0);
            //MySQLのtinyintはsbyte(かつて両経路ともTextFieldに落ちていたギャップの解消)
            AssertNumber(Convert(DataSourceType.MySQL, "tinyint", typeof(sbyte).FullName!), 0);
        }

        [Test]
        public void 実数列は桁制限なしのNumberField()
        {
            AssertNumber(Convert(DataSourceType.PostgreSQL, "double precision", typeof(double).FullName!), null);
            AssertNumber(Convert(DataSourceType.PostgreSQL, "numeric", typeof(decimal).FullName!), null);
            AssertNumber(Convert(DataSourceType.Oracle, "NUMBER", typeof(decimal).FullName!), null);
            AssertNumber(Convert(DataSourceType.SQLServer, "real", typeof(float).FullName!), null);
        }

        [Test]
        public void 文字列系はTextField()
        {
            AssertType<TextFieldDesign>(Convert(DataSourceType.PostgreSQL, "text", typeof(string).FullName!));
            AssertType<TextFieldDesign>(Convert(DataSourceType.PostgreSQL, "uuid", typeof(Guid).FullName!));
            AssertType<TextFieldDesign>(Convert(DataSourceType.SQLServer, "sql_variant", typeof(object).FullName!));
            //未解決DB固有型(RawDbValue)はTextField
            AssertType<TextFieldDesign>(Convert(DataSourceType.PostgreSQL, "interval", "Codeer.LowCode.Blazor.DataIO.Db.RawDbValue"));
            AssertType<TextFieldDesign>(Convert(DataSourceType.Oracle, "ROWID", "Codeer.LowCode.Blazor.DataIO.Db.RawDbValue"));
        }

        [Test]
        public void 日付時刻系()
        {
            AssertType<DateFieldDesign>(Convert(DataSourceType.PostgreSQL, "date", typeof(DateOnly).FullName!));
            AssertType<DateFieldDesign>(Convert(DataSourceType.MySQL, "date", typeof(DateOnly).FullName!));
            AssertType<DateTimeFieldDesign>(Convert(DataSourceType.SQLServer, "date", typeof(DateTime).FullName!));
            AssertType<TimeFieldDesign>(Convert(DataSourceType.PostgreSQL, "time", typeof(TimeSpan).FullName!));
            AssertType<TimeFieldDesign>(Convert(DataSourceType.SQLite, "time", typeof(TimeOnly).FullName!));
            AssertType<TimeFieldDesign>(Convert(DataSourceType.Oracle, "INTERVAL DAY TO SECOND", typeof(TimeSpan).FullName!));
        }

        [Test]
        public void オフセット保存列はDateTimeOffsetField()
        {
            AssertType<DateTimeOffsetFieldDesign>(Convert(DataSourceType.PostgreSQL, "time with time zone", typeof(DateTimeOffset).FullName!));
            AssertType<DateTimeOffsetFieldDesign>(Convert(DataSourceType.Oracle, "TIMESTAMP WITH TIME ZONE", typeof(DateTimeOffset).FullName!));
            AssertType<DateTimeOffsetFieldDesign>(Convert(DataSourceType.SQLServer, "datetimeoffset", typeof(DateTimeOffset).FullName!));
        }

        [Test]
        public void UTC瞬間列はSaveAsUtcが既定ON()
        {
            static bool Utc(FieldDesignBase f) => ((DateTimeFieldDesign)f).SaveAsUtc;

            Assert.That(Utc(Convert(DataSourceType.PostgreSQL, "timestamp with time zone", typeof(DateTime).FullName!)), Is.True);
            Assert.That(Utc(Convert(DataSourceType.MySQL, "timestamp", typeof(DateTime).FullName!)), Is.True);
            Assert.That(Utc(Convert(DataSourceType.Oracle, "TIMESTAMP WITH LOCAL TIME ZONE", typeof(DateTime).FullName!)), Is.True);

            //ローカル時刻列はOFFのまま
            Assert.That(Utc(Convert(DataSourceType.PostgreSQL, "timestamp without time zone", typeof(DateTime).FullName!)), Is.False);
            Assert.That(Utc(Convert(DataSourceType.MySQL, "datetime", typeof(DateTime).FullName!)), Is.False);
            Assert.That(Utc(Convert(DataSourceType.SQLite, "timestamp", typeof(DateTime).FullName!)), Is.False);
        }

        [Test]
        public void バイナリ列はFileFieldのファイル実体列()
        {
            foreach (var (type, raw) in new[]
            {
                (DataSourceType.PostgreSQL, "bytea"),
                (DataSourceType.Oracle, "BLOB"),
                (DataSourceType.SQLServer, "varbinary"),
                (DataSourceType.MySQL, "longblob"),
                (DataSourceType.SQLite, "blob"),
            })
            {
                var field = Convert(type, raw, typeof(byte[]).FullName!);
                Assert.That(field, Is.TypeOf<FileFieldDesign>(), raw);
                Assert.That(((FileFieldDesign)field).DbColumnFileContent, Is.EqualTo("col1"), raw);
                Assert.That(DbColumnOf(field), Is.Null.Or.Empty, raw);
            }
        }

        [Test]
        public void OracleのINTERVAL_YEARは総月数のNumberField()
            => AssertNumber(Convert(DataSourceType.Oracle, "INTERVAL YEAR TO MONTH", "Codeer.LowCode.Blazor.DataIO.Db.RawDbValue"), 0);

        [Test]
        public void NOT_NULL列はIsRequired()
        {
            Assert.That(((ValueFieldDesignBase)Convert(DataSourceType.PostgreSQL, "integer", typeof(int).FullName!, isNullable: false)).IsRequired, Is.True);
            Assert.That(((ValueFieldDesignBase)Convert(DataSourceType.PostgreSQL, "integer", typeof(int).FullName!, isNullable: true)).IsRequired, Is.False);
        }

        [Test]
        public void 真偽列はBooleanField()
        {
            AssertType<BooleanFieldDesign>(Convert(DataSourceType.PostgreSQL, "boolean", typeof(bool).FullName!));
            AssertType<BooleanFieldDesign>(Convert(DataSourceType.SQLServer, "bit", typeof(bool).FullName!));
        }

        // ===== ConvertForModule (命名・システム予約名の正規化。ドロップの AdjustNewField と同一経路) =====

        static ModuleDesign NewModule() => new() { Name = "Item", DataSourceName = "Main", DbTable = "t" };

        static FieldDesignBase ConvertForModule(ModuleDesign module, string columnName, string rawDbType, string netTypeFullName)
        {
            var dataSource = new DataSource { Name = "Main", DataSourceType = DataSourceType.PostgreSQL };
            var col = new DbColumnDefinition { Name = columnName, NetTypeFullName = netTypeFullName, RawDbTypeName = rawDbType, IsNullable = true };
            var table = new DbTableDefinition { Name = "t", Columns = { col } };
            return DbColumnFieldConverter.ConvertForModule(module, dataSource, table, col);
        }

        [Test]
        public void 通常列はスネークケースからタイトルケース()
        {
            var field = ConvertForModule(NewModule(), "user_name", "character varying", typeof(string).FullName!);
            Assert.That(field.Name, Is.EqualTo("UserName"));
            Assert.That(field, Is.TypeOf<TextFieldDesign>());
            Assert.That(DbColumnOf(field), Is.EqualTo("user_name"));
        }

        [Test]
        public void システム予約名は該当型のシステムフィールドに正規化される()
        {
            var id = ConvertForModule(NewModule(), "id", "bigint", typeof(long).FullName!);
            Assert.That(id, Is.TypeOf<IdFieldDesign>());
            Assert.That(id.Name, Is.EqualTo(SystemFieldNames.Id));
            Assert.That(DbColumnOf(id), Is.EqualTo("id"));

            var logicalDelete = ConvertForModule(NewModule(), "logical_delete", "boolean", typeof(bool).FullName!);
            Assert.That(logicalDelete, Is.TypeOf<BooleanFieldDesign>());
            Assert.That(logicalDelete.Name, Is.EqualTo(SystemFieldNames.LogicalDelete));

            var createdAt = ConvertForModule(NewModule(), "created_at", "timestamp without time zone", typeof(DateTime).FullName!);
            Assert.That(createdAt, Is.TypeOf<DateTimeFieldDesign>());
            Assert.That(createdAt.Name, Is.EqualTo(SystemFieldNames.CreatedAt));
            Assert.That(((DateTimeFieldDesign)createdAt).SaveAsUtc, Is.True); //システム監査列はUTC保存

            //文字列のcreatorはLinkField(かつて取込がCreator_に逃がしていたバグの固定)
            var creator = ConvertForModule(NewModule(), "creator", "character varying", typeof(string).FullName!);
            Assert.That(creator, Is.TypeOf<LinkFieldDesign>());
            Assert.That(creator.Name, Is.EqualTo(SystemFieldNames.Creator));

            var locking = ConvertForModule(NewModule(), "optimistic_locking", "bigint", typeof(long).FullName!);
            Assert.That(locking, Is.TypeOf<OptimisticLockingFieldDesign>());
            Assert.That(locking.Name, Is.EqualTo(SystemFieldNames.OptimisticLocking));
        }

        [Test]
        public void 予約名でも列型が合わなければ末尾アンダースコアで通常フィールド()
        {
            //text列のcreated_atは監査列ではない(偶然の名前衝突)
            var field = ConvertForModule(NewModule(), "created_at", "text", typeof(string).FullName!);
            Assert.That(field, Is.TypeOf<TextFieldDesign>());
            Assert.That(field.Name, Is.EqualTo(SystemFieldNames.CreatedAt + "_"));
            Assert.That(DbColumnOf(field), Is.EqualTo("created_at"));
        }

        [Test]
        public void 既存フィールドと重複したら連番()
        {
            var module = NewModule();
            var first = ConvertForModule(module, "user_name", "text", typeof(string).FullName!);
            module.Fields.Add(first);
            var second = ConvertForModule(module, "user_name", "text", typeof(string).FullName!);
            Assert.That(first.Name, Is.EqualTo("UserName"));
            Assert.That(second.Name, Is.EqualTo("UserName1"));
        }
    }
}

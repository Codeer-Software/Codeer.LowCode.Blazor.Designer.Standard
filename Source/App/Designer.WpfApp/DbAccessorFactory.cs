using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.Designer;
using Codeer.LowCode.Blazor.SystemSettings;
using Codeer.LowCode.Blazor.DbAccess;

namespace Designer.WpfApp
{
    public class DbAccessorFactory : IDbAccessorFactory
    {
        public IDbAccessor Create(DataSource[] dataSources) => new DbAccessor(dataSources);
    }
}

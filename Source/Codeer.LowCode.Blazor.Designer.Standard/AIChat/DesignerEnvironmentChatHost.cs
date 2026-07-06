using System.Windows;
using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.Designer.Standard.Views;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat
{
    // 本番用の IDesignerChatHost 実装。DesignerEnvironment をラップし、DDL はモードレスの DDLWindow で提示する。
    public sealed class DesignerEnvironmentChatHost : IDesignerEnvironmentChatHost
    {
        readonly DesignerEnvironment _env;

        public DesignerEnvironmentChatHost(DesignerEnvironment env) => _env = env;

        public DesignerEnvironment DesignerEnvironment => _env;

        public DesignData GetDesignData() => _env.GetDesignData();

        //チャット層(AIChat)へは接続文字列を空にしたコピーを渡す。
        //各機能はプロンプトに Name / DataSourceType しか書かない規律だが、将来 DataSource を
        //まるごとシリアライズする実装が入っても接続文字列がLLMへ届かないよう、境界で物理的に落とす。
        public IReadOnlyList<DataSource> GetDataSources()
            => _env.GetDesignerSettings().DataSources
                .Select(d => new DataSource
                {
                    Name = d.Name,
                    DataSourceType = d.DataSourceType,
                    AllowCliSqlAccess = d.AllowCliSqlAccess,
                    ConnectionString = string.Empty,
                })
                .ToList();

        public List<DbTableDefinition> GetDbInfo(string dataSourceName) => _env.GetDbInfo(dataSourceName);

        public string CurrentFileDirectory => _env.CurrentFileDirectory;

        public void ShowDdl(DataSource dataSource, string ddl)
        {
            var app = Application.Current;
            if (app == null) return;

            //受け取る dataSource は GetDataSources のサニタイズ済みコピー。
            //DDLWindow は DDL を実際に DB へ実行するため、名前から接続文字列込みの実体を引き直す。
            var fullDataSource = _env.GetDesignerSettings().DataSources
                .FirstOrDefault(d => d.Name == dataSource.Name) ?? dataSource;

            app.Dispatcher.Invoke(() =>
            {
                new DDLWindow
                {
                    DesignerEnvironment = _env,
                    DataSource = fullDataSource,
                    DisplayText = ddl,
                    Owner = app.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Title = "DDL",
                }.Show();
            });
        }
    }
}

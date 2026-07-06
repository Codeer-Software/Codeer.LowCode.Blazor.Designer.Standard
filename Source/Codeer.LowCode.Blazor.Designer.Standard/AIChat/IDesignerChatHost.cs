using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat
{
    // 各 AI チャットがデザイナ環境へアクセスするための抽象。本番は DesignerEnvironmentChatHost、テストはフェイクを差し込む。
    public interface IDesignerChatHost
    {
        DesignData GetDesignData();
        IReadOnlyList<DataSource> GetDataSources();
        List<DbTableDefinition> GetDbInfo(string dataSourceName);
        string CurrentFileDirectory { get; }

        // 生成した DDL をユーザーに提示する(本番: モードレスの DDLWindow / テスト: 記録のみ)。
        void ShowDdl(DataSource dataSource, string ddl);
    }

    // 各画面へのチャット結線(DesignerChatRegistration)専用のホスト。DesignerEnvironment を公開する。
    // チャット本体には env を持たない IDesignerChatHost だけを渡すため、この派生は結線側でのみ使う。
    public interface IDesignerEnvironmentChatHost : IDesignerChatHost
    {
        DesignerEnvironment DesignerEnvironment { get; }
    }
}

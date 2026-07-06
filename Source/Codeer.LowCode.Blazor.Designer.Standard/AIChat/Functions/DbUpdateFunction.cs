using Microsoft.Extensions.AI;
using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.DesignLogic;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // DBアクセス(DDL生成)機能。現在のモジュール定義と実DBスキーマの差分から DDL(CREATE/ALTER/INDEX 等)を生成し、
    // DDLウィンドウに提示する(実行はユーザーが行う。自動実行はしない)。
    // これは物理DBに影響する操作なので、全体設定画面からのみ使えるようにする(FunctionCatalog のマップで制御)。
    // フィールド作成/編集(field.create/field.edit)自体はDBに触れずデザインのみ変更し、DB反映はこの機能で明示的に行う。
    internal class DbUpdateFunction : IAiFunction
    {
        readonly IFieldContext _editor;
        readonly IDesignerChatHost _host;
        readonly ModuleDdlGenerator _ddlGenerator;

        public string Id => FunctionCatalog.DbUpdate;
        public string DisplayName => FunctionCatalog.Entries[FunctionCatalog.DbUpdate].DisplayName;
        public string RouterDescription => FunctionCatalog.Entries[FunctionCatalog.DbUpdate].RouterDescription;

        public DbUpdateFunction(IDesignerChatHost host, Func<IChatClient> createChatClient, IFieldContext editor)
        {
            _host = host;
            _editor = editor;
            _ddlGenerator = new ModuleDdlGenerator(createChatClient);
        }

        public void Clear() { }

        public async Task<FunctionResult> ExecuteAsync(string instruction)
        {
            var module = _editor.GetModuleDesign();

            if (string.IsNullOrEmpty(module.DataSourceName) || string.IsNullOrEmpty(module.DbTable))
                return FunctionResult.NothingToDo(
                    "このモジュールにはデータソース/DBテーブルが設定されていないため、DBスキーマを更新できません。先に全体設定でデータソースとテーブルを設定してください。");

            var dataSource = _host.GetDataSources().FirstOrDefault(d => d.Name == module.DataSourceName);
            if (dataSource == null)
                return FunctionResult.NothingToDo($"データソース '{module.DataSourceName}' が見つかりません。");

            try
            {
                var existing = _host.GetDbInfo(dataSource.Name);
                var baseline = module.CreateDDL(dataSource.DataSourceType, existing);
                // AI は「実行する部分」だけを作る(CREATE / 不足列 ALTER ADD の型精緻化・インデックス・明示要求のDROP)。
                var executable = await _ddlGenerator.GenerateAsync(module, dataSource.DataSourceType, existing, baseline, instruction);

                // 「作り直し用 DROP+CREATE」「列削除候補 DROP COLUMN」の参考コメントは、コードが決定的に(毎回同じ形で)付ける。
                // = 安全な足場は機械生成で不変、AI はその上に実行DDLを上乗せ。
                var scaffolding = module.CreateDdlReferenceComments(dataSource.DataSourceType, existing);

                var noExec = string.IsNullOrWhiteSpace(executable) || executable.Contains("変更は不要");
                if (noExec && scaffolding.Count == 0)
                    return FunctionResult.NothingToDo("現在のモジュール定義に対して、DBスキーマの変更は不要です。");

                var body = noExec ? $"-- {module.DbTable}: 追加・変更する列はありません" : executable.Trim();
                var ddl = scaffolding.Count == 0
                    ? body
                    : string.Join(Environment.NewLine, scaffolding) + Environment.NewLine + body;

                _host.ShowDdl(dataSource, ddl);
                return FunctionResult.Done(
                    "DBスキーマ更新のDDLを生成し、DDLウィンドウに表示しました。内容を確認して実行ボタンで実行してください(自動実行はしません)。");
            }
            catch (Exception ex)
            {
                return FunctionResult.Error($"DDLの生成中にエラーが発生しました: {ex.Message}");
            }
        }
    }
}

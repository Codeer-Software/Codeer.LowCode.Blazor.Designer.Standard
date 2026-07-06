using Microsoft.Extensions.AI;
using System.Linq;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // SQL/Query 画面の「自分のフィールド設定編集」機能。
    // 汎用フィールド操作(他フィールドの作成/編集)はせず、この画面が編集する 1 フィールド(ExecuteSql/Query フィールド)自身の
    // プロパティ(実行タイミング・入出力・各種設定など SQL 文以外)だけを編集する。SQL 文の作成・改善は sql.edit が担う。
    // 汎用の編集ラウンドトリップ(生成→検証→適用/取消)は ModuleEditFunctionBase を再利用し、Apply で対象フィールドだけを差し替える。
    internal class SqlFieldSettingsFunction : ModuleEditFunctionBase
    {
        readonly string _fieldName;

        public SqlFieldSettingsFunction(IDesignerChatHost host, Func<IChatClient> createChatClient, IFieldSelfContext editor)
            : base(host, createChatClient, editor) => _fieldName = editor.GetFieldName();

        public override string Id => FunctionCatalog.SqlFieldSettings;
        public override string DisplayName => FunctionCatalog.Entries[FunctionCatalog.SqlFieldSettings].DisplayName;
        public override string RouterDescription => FunctionCatalog.Entries[FunctionCatalog.SqlFieldSettings].RouterDescription;

        // 出力から対象フィールドだけを取り出し、既存モジュールの該当フィールドを差し替える。他フィールドは一切変更しない。
        protected override void Apply(ModuleDesign editingModule, IO output)
        {
            var updated = output.ModuleDesign.Fields.FirstOrDefault(f => f.Name == _fieldName);
            if (updated == null) return;
            var index = editingModule.Fields.FindIndex(f => f.Name == _fieldName);
            if (index >= 0) editingModule.Fields[index] = updated;
        }

        protected override string SystemPrompt => $@"
あなたはローコードWebアプリケーションの「{_fieldName}」フィールド(この画面が編集する ExecuteSql / Query フィールド)自身の設定編集担当です。
ユーザーの指示に基づき、このフィールド自身のプロパティ(実行タイミング・入出力・各種設定など SQL 文以外)を変更し、結果をJSONで返してください。
各フィールド型のプロパティ・共通基底・TypeFullName は、別途渡される「## モジュール設定仕様」に必ず従ってください。

## この機能のルール
- **編集対象は「{_fieldName}」フィールドだけ**です。他のフィールドの追加・編集・リネーム・削除は行いません。
- 出力する ModuleDesign.Fields は**現在の全フィールドをそのまま含め**、「{_fieldName}」のプロパティだけを指示どおりに書き換えてください(他フィールドは現在値のまま)。
- 「{_fieldName}」の Name は変更しないでください(このフィールドのリネームはこの機能では行いません)。
- **SQL 文そのものの作成・改善はこの機能では行いません**(それは『SQL編集』で行います)。ここではフィールドの設定値を扱います。
- 指示のないプロパティは現在値のまま返してください(必要最小限の変更)。
- この機能はデザイン(フィールド定義)だけを変更し、物理DBには触れません。";
    }
}

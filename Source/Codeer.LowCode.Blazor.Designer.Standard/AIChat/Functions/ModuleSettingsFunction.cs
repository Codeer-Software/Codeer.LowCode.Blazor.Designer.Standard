using Microsoft.Extensions.AI;
using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // モジュール設定編集機能。モジュール全体の設定(データソース・DBテーブル・CRUD可否・各権限条件)を変更する。物理DB反映(DDL)は別機能 db.update。
    // フィールドの追加/編集は行わない(全体設定画面のみで提供)。適用は ApplySettings によりフィールドを触らないよう強制される。
    internal class ModuleSettingsFunction : ModuleEditFunctionBase
    {
        public ModuleSettingsFunction(IDesignerChatHost host, Func<IChatClient> createChatClient, IFieldContext editor)
            : base(host, createChatClient, editor) { }

        public override string Id => FunctionCatalog.ModuleSettings;
        public override string DisplayName => FunctionCatalog.Entries[FunctionCatalog.ModuleSettings].DisplayName;
        public override string RouterDescription => FunctionCatalog.Entries[FunctionCatalog.ModuleSettings].RouterDescription;

        protected override void Apply(ModuleDesign editingModule, IO output) => ApplySettings(editingModule, output);

        protected override string SystemPrompt => @"
あなたはローコードWebアプリケーションの「モジュール全体の設定」を編集する担当です。
ユーザーの指示に基づいて、データソース・DBテーブル・作成/更新/削除の可否・ユーザー/データの読み書き権限条件を変更し、結果をJSONで返してください。
権限条件(ModuleMatchCondition / FieldValueMatchCondition / FieldVariableMatchCondition)の書き方・TypeFullName は、別途渡される「## モジュール設定仕様」に必ず従ってください。

## この機能のルール
- **あなたが編集できるのはモジュール設定だけ**です: DataSourceName / DbTable / CanCreate / CanUpdate / CanDelete / UserWriteCondition / UserReadCondition / DataWriteCondition / DataReadCondition。**フィールド(Fields)の追加・編集・削除はこの機能では行いません(Fields への変更は無視されます)。** フィールドを変更したい指示なら Explanation にその旨を書いてください(フィールドは「フィールド作成」「フィールド編集」で行います)。
- 出力の ModuleDesign.Fields は現在値をそのまま返してください(この機能では変更されません)。
- 必要最小限の変更にしてください。指示のない設定は現在値のまま返します。ModuleName は変更しないでください。
- **DbTable/DataSourceName**: 指定された(新規作成予定を含む)名前はそのまま採用し、存在しないからと勝手に空へ戻さないでください。空にするのはユーザーが明示的に連携解除を指示したときだけです。
- この機能はデザイン(モジュール設定)だけを変更し、物理DBには触れません。DBスキーマへの反映(DDL)は『DB更新』機能で別途行います。";
    }
}

using Microsoft.Extensions.AI;
using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Designer.Standard.AIChat.Functions
{
    // フィールド編集機能。既存フィールドのプロパティ変更・リネーム・削除を行う(新規追加、モジュール設定変更はしない)。
    // 適用は ApplyAllFields(Fields 全置換)により、リネーム(Name 変更)や削除(返さない)も反映できる。
    internal class FieldEditFunction : ModuleEditFunctionBase
    {
        public FieldEditFunction(IDesignerChatHost host, Func<IChatClient> createChatClient, IFieldContext editor)
            : base(host, createChatClient, editor) { }

        public override string Id => FunctionCatalog.FieldEdit;
        public override string DisplayName => FunctionCatalog.Entries[FunctionCatalog.FieldEdit].DisplayName;
        public override string RouterDescription => FunctionCatalog.Entries[FunctionCatalog.FieldEdit].RouterDescription;

        protected override void Apply(ModuleDesign editingModule, IO output) => ApplyAllFields(editingModule, output);

        protected override string SystemPrompt => @"
あなたはローコードWebアプリケーションのモジュールの「既存フィールドの編集」担当です。
ユーザーの指示に基づいて既存フィールドのプロパティ変更(表示名・最大長・最大値・必須・候補・検索条件・イベント名など)、リネーム、削除を行い、結果をJSONで返してください。
各フィールド型のプロパティ・共通基底・検索条件・TypeFullName は、別途渡される「## モジュール設定仕様」に必ず従ってください。

## この機能のルール
- **あなたができるのは「既存フィールドの編集(プロパティ変更・リネーム・削除)」**です。**新しいフィールドの新規追加**、およびモジュール設定(データソース/テーブル/CRUD可否/権限条件)の変更はこの機能では行いません(設定への変更は無視されます。追加は「フィールド作成」で行います)。
- 出力する ModuleDesign.Fields は**編集後のフィールド一覧全体**です。変更しないフィールドもすべて含め、変更対象だけプロパティを書き換えてください。**リネーム**は該当フィールドの Name(および必要なら DbColumn)を新しい値にします。**削除**は そのフィールドを Fields に含めないことで表します。指示に無いフィールドを勝手に消さないでください。
- 指示のないプロパティ・フィールドは現在値のまま返してください(必要最小限の変更)。
- **イベントの設定**: OnClick / OnDataChanged 等のイベントプロパティには、呼び出すスクリプト関数名を設定できます。指定した関数がまだ無ければ、**ツールが自動でスクリプトに空のハンドラ関数を作成します**(あなたはスクリプト本文を書きません)。関数名はユーザーの指定があればそれを、無ければ `{フィールド名}_{イベント名}` 形式(例: SaveButton_OnClick)にしてください。イベントを解除するときは空文字にします。処理の中身の実装は「スクリプト編集」の担当です。
- 新しいフィールドを追加する指示だった場合、この機能では追加できません。Explanation にその旨を書いてください(追加は「フィールド作成」で行います)。
- モジュール設定(DataSourceName/DbTable など)は現在値をそのまま返してください(この機能では変更されません)。
- この機能はデザイン(フィールド定義)だけを変更し、物理DBには触れません。DBスキーマへの反映(列の削除・型変更等のDDL)は全体設定の『DB更新』で別途行います。";
    }
}

using Codeer.LowCode.Blazor.Designer.Models;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;

namespace Codeer.LowCode.Blazor.Designer.Standard
{
    /// <summary>
    /// デザイナ標準のプロジェクトテンプレート。
    /// <see cref="AddAll"/> で全件登録できるほか、個別メソッドで必要なものだけ
    /// DesignerTemplateCandidate.Templates に追加したり、Name / Description を
    /// 書き換えてから登録することもできる。
    /// </summary>
    public static class StandardTemplates
    {
        const string ResourcePrefix = "Codeer.LowCode.Blazor.Designer.Standard.Templates.";
        const string LocalDataDir = @"C:\Codeer.LowCode.Blazor.Local\Data";

        public static DesignerTemplate Empty() => new()
        {
            Create = path => CreateProject(path, "EmptyTemplate.bin", "sqlite_sample.db"),
            Name = "空のプロジェクト",
            Description = "最小構成の空プロジェクト。モジュール / ページフレームを 1 から作りたいときに。",
        };

        public static DesignerTemplate EmptyAuth() => new()
        {
            Create = path => CreateProject(path, "EmptyAuthTemplate.bin", "sqlite_sample_auth.db"),
            Name = "空のプロジェクト（認証付き）",
            Description = "Cookie 認証付きの空プロジェクト。AppUser モジュールとログインまわりの最小構成が組み込み済。初期ユーザーは admin/admin。\n※Visual Studio で新規ソリューションを作成するときは「Codeer.LowCode.Blazor.Cookie」で作成してください。その他のものとは整合しません。",
        };

        public static DesignerTemplate GettingStarted() => new()
        {
            Create = path => CreateProject(path, "GettingStartedTemplate.bin", "sqlite_sample.db"),
            Name = "入門サンプル",
            Description = "Codeer.LowCode.Blazor を初めて触る方向けの入門用サンプル。著者管理・書籍登録などの最小限の業務画面を一通り含み、デザイナの基本操作を覚えるのに使えます。",
        };

        public static DesignerTemplate PatternShowcase() => new()
        {
            Create = path => CreateProject(path, "PatternShowcaseTemplate.bin", "sqlite_patterns_v4.db"),
            Name = "標準パターン集",
            Description = "Codeer.LowCode.Blazor で実現できる標準パターンを集めたサンプル集 (データ操作 / 検索 / リスト / 一覧 / ダイアログ / レイアウト / 入力UX / 出力 / 別フレーム など 50 種以上)。各機能の実装例として参考にしてください。",
        };

        public static DesignerTemplate PatternShowcaseAuth() => new()
        {
            Create = path => CreateProject(path, "PatternShowcaseAuthTemplate.bin", "sqlite_patterns_auth_v3.db"),
            Name = "認証パターン集",
            Description = "Cookie 認証を組み込んだ認証・権限パターンのサンプル集 (ユーザー管理 / マイプロフィール / アプリ権限 / PageFrame 権限 / 作成者更新者の自動記録 / 行レベルセキュリティ / 自分宛タスク など)。初期ユーザー: admin/admin、alice/test、bob/test、carol/test、dave/test。\n※Visual Studio で新規ソリューションを作成するときは「Codeer.LowCode.Blazor.Cookie」で作成してください。その他のものとは整合しません。",
        };

        public static DesignerTemplate InventoryManagement() => new()
        {
            Create = path => CreateProject(path, "InventoryManagementTemplate.bin", "inventory_v1.db"),
            Name = "在庫管理テンプレート",
            Description = "倉庫の入庫・出庫・棚卸し・発注など、在庫管理業務を一通り含む業務テンプレート。複数倉庫や商品マスタの扱いの参考に。",
        };

        public static DesignerTemplate Sfa() => new()
        {
            Create = path => CreateProject(path, "SFATemplate.bin", "sfa_v1.db"),
            Name = "営業支援 (SFA) テンプレート",
            Description = "顧客 / 商談 / 活動履歴 / 案件パイプラインなど、営業支援 (SFA) 業務を一通り含む業務テンプレート。営業案件の進捗管理の参考に。",
        };

        public static DesignerTemplate ProjectManagement() => new()
        {
            Create = path => CreateProject(path, "ProjectManagementTemplate.bin", "project_management_v1.db"),
            Name = "プロジェクト管理テンプレート",
            Description = "プロジェクト / タスク / 工数 / 進捗管理など、プロジェクト管理業務を一通り含む業務テンプレート。タスク階層やガントチャート的な可視化の参考に。",
        };

        public static List<DesignerTemplate> All() =>
        [
            Empty(),
            EmptyAuth(),
            GettingStarted(),
            PatternShowcase(),
            PatternShowcaseAuth(),
            InventoryManagement(),
            Sfa(),
            ProjectManagement(),
        ];

        /// <summary>標準テンプレートを全件 DesignerTemplateCandidate に登録する。</summary>
        public static void AddAll() => DesignerTemplateCandidate.Templates.AddRange(All());

        // テンプレート zip を展開し、テンプレートが参照するサンプル DB をローカルに展開する。
        static void CreateProject(string path, string templateResource, string sampleDbResource)
        {
            using var stream = LoadResource(templateResource);
            ZipFile.ExtractToDirectory(stream, path);
            EnsureSampleDbExtracted(Path.Combine(LocalDataDir, sampleDbResource), sampleDbResource);
        }

        static Stream LoadResource(string name)
            => typeof(StandardTemplates).Assembly.GetManifestResourceStream(ResourcePrefix + name)
               ?? throw new InvalidOperationException($"embedded resource not found: {ResourcePrefix + name}");

        /// <summary>
        /// サンプル DB を展開する。ファイル無し、または 0 byte (Server が空ファイルを先に作っていた場合)
        /// なら上書き。ファイルが Server にロックされていて書けないときは案内メッセージを表示。
        /// </summary>
        static void EnsureSampleDbExtracted(string dbPath, string resourceName)
        {
            if (File.Exists(dbPath) && new FileInfo(dbPath).Length > 0) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
                using var stream = LoadResource(resourceName);
                using var file = File.Create(dbPath);
                stream.CopyTo(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(
                    $"サンプル DB の展開に失敗しました。Server プロジェクトが起動中の場合は停止してから新規作成をやり直してください。\n\n対象: {dbPath}\nエラー: {ex.Message}",
                    "DB 展開エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}

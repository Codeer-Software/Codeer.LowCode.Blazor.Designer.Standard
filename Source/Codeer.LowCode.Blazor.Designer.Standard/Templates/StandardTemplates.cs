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
    /// ProjectCatalog.Add に渡したり、Name / Description を書き換えてから登録することもできる。
    ///
    /// headless CLI (template-list / template-extract / ai-refresh) からも参照されるため、
    /// 登録はアプリの OnStartup で base.OnStartup(e) より前に行う (ProjectCatalog.Add は冪等なので、
    /// base.OnStartup 後の DesignerStandard.Setup と併用しても二重登録にはならない)。
    /// </summary>
    public static class StandardTemplates
    {
        const string ResourcePrefix = "Codeer.LowCode.Blazor.Designer.Standard.Templates.";
        const string LocalDataDir = @"C:\Codeer.LowCode.Blazor.Local\Data";

        public static ProjectCatalogEntry Empty() => Make(
            "Empty", "EmptyTemplate.bin", "sqlite_sample.db",
            "空のプロジェクト",
            "最小構成の空プロジェクト。モジュール / ページフレームを 1 から作りたいときに。");

        public static ProjectCatalogEntry EmptyAuth() => Make(
            "EmptyAuth", "EmptyAuthTemplate.bin", "sqlite_sample_auth.db",
            "空のプロジェクト（認証付き）",
            "Cookie 認証付きの空プロジェクト。AppUser モジュールとログインまわりの最小構成が組み込み済。初期ユーザーは admin/admin。\n※Visual Studio で新規ソリューションを作成するときは「Codeer.LowCode.Blazor.Cookie」で作成してください。その他のものとは整合しません。");

        public static ProjectCatalogEntry GettingStarted() => Make(
            "GettingStarted", "GettingStartedTemplate.bin", "sqlite_sample.db",
            "入門サンプル",
            "Codeer.LowCode.Blazor を初めて触る方向けの入門用サンプル。著者管理・書籍登録などの最小限の業務画面を一通り含み、デザイナの基本操作を覚えるのに使えます。");

        public static ProjectCatalogEntry PatternShowcase() => Make(
            "PatternShowcase", "PatternShowcaseTemplate.bin", "sqlite_patterns_v4.db",
            "標準パターン集",
            "Codeer.LowCode.Blazor で実現できる標準パターンを集めたサンプル集 (データ操作 / 検索 / リスト / 一覧 / ダイアログ / レイアウト / 入力UX / 出力 / 別フレーム など 50 種以上)。各機能の実装例として参考にしてください。");

        public static ProjectCatalogEntry PatternShowcaseAuth() => Make(
            "PatternShowcaseAuth", "PatternShowcaseAuthTemplate.bin", "sqlite_patterns_auth_v3.db",
            "認証パターン集",
            "Cookie 認証を組み込んだ認証・権限パターンのサンプル集 (ユーザー管理 / マイプロフィール / アプリ権限 / PageFrame 権限 / 作成者更新者の自動記録 / 行レベルセキュリティ / 自分宛タスク など)。初期ユーザー: admin/admin、alice/test、bob/test、carol/test、dave/test。\n※Visual Studio で新規ソリューションを作成するときは「Codeer.LowCode.Blazor.Cookie」で作成してください。その他のものとは整合しません。");

        public static ProjectCatalogEntry InventoryManagement() => Make(
            "InventoryManagement", "InventoryManagementTemplate.bin", "inventory_v1.db",
            "在庫管理テンプレート",
            "倉庫の入庫・出庫・棚卸し・発注など、在庫管理業務を一通り含む業務テンプレート。複数倉庫や商品マスタの扱いの参考に。");

        public static ProjectCatalogEntry Sfa() => Make(
            "SFA", "SFATemplate.bin", "sfa_v1.db",
            "営業支援 (SFA) テンプレート",
            "顧客 / 商談 / 活動履歴 / 案件パイプラインなど、営業支援 (SFA) 業務を一通り含む業務テンプレート。営業案件の進捗管理の参考に。");

        public static ProjectCatalogEntry ProjectManagement() => Make(
            "ProjectManagement", "ProjectManagementTemplate.bin", "project_management_v1.db",
            "プロジェクト管理テンプレート",
            "プロジェクト / タスク / 工数 / 進捗管理など、プロジェクト管理業務を一通り含む業務テンプレート。タスク階層やガントチャート的な可視化の参考に。");

        public static List<ProjectCatalogEntry> All() =>
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

        /// <summary>
        /// 標準テンプレートを全件 ProjectCatalog に登録する
        /// (冪等は ProjectCatalog.Add 側が保証。base.OnStartup 前の登録と
        /// DesignerStandard.Setup の両方から呼ばれても二重登録しない)。
        /// </summary>
        public static void AddAll()
        {
            foreach (var template in All())
            {
                ProjectCatalog.Add(template);
            }
        }

        static ProjectCatalogEntry Make(string folderName, string templateResource, string sampleDbResource, string name, string description) => new()
        {
            FolderName = folderName,
            Name = name,
            Description = description,
            Create = path => CreateProject(path, templateResource, sampleDbResource),
            // headless CLI の参照用サンプル展開。プロジェクトファイルのみ (サンプル DB 配置・UI なし)。
            ExtractProjectFiles = path => ExtractZip(path, templateResource),
        };

        // テンプレート zip を展開し、テンプレートが参照するサンプル DB をローカルに展開する。
        static void CreateProject(string path, string templateResource, string sampleDbResource)
        {
            ExtractZip(path, templateResource);
            EnsureSampleDbExtracted(Path.Combine(LocalDataDir, sampleDbResource), sampleDbResource);
        }

        static void ExtractZip(string path, string templateResource)
        {
            using var stream = LoadResource(templateResource);
            ZipFile.ExtractToDirectory(stream, path);
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

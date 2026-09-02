using Codeer.LowCode.Blazor.Designer.Extensibility;
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

        // すべてのテンプレートは Cookie 認証ホスト (Codeer.LowCode.Blazor.Starter / VS テンプレート "Codeer.LowCode.Blazor") で動く前提:
        // AppUser モジュール + app_users テーブル (初期ユーザー admin/admin) を含む。認証なしのテンプレートは提供しない。

        public static ProjectCatalogEntry Empty() => Make(
            "Empty", "EmptyTemplate.bin", "sqlite_sample_v2.db",
            "空のプロジェクト",
            "最小構成の空プロジェクト。AppUser モジュールとログインまわりだけを含み、モジュール / ページフレームを 1 から作りたいときに。初期ユーザーは admin/admin。");

        public static ProjectCatalogEntry GettingStarted() => Make(
            "GettingStarted", "GettingStartedTemplate.bin", "sqlite_sample_v2.db",
            "入門サンプル",
            "Codeer.LowCode.Blazor を初めて触る方向けの入門用サンプル。著者管理・書籍登録などの最小限の業務画面を一通り含み、デザイナの基本操作を覚えるのに使えます。初期ユーザーは admin/admin。");

        public static ProjectCatalogEntry PatternShowcase() => Make(
            "PatternShowcase", "PatternShowcaseTemplate.bin", "sqlite_patterns_v5.db",
            "標準パターン集",
            "Codeer.LowCode.Blazor で実現できる標準パターンを集めたサンプル集 (データ操作 / 検索 / リスト / 一覧 / ダイアログ / レイアウト / 入力UX / 出力 / 別フレーム / 認証・権限 / 承認フロー など 60 種以上)。各機能の実装例として参考にしてください。初期ユーザー: admin/admin、alice/test、bob/test、carol/test、dave/test。");

        public static ProjectCatalogEntry InventoryManagement() => Make(
            "InventoryManagement", "InventoryManagementTemplate.bin", "inventory_v2.db",
            "在庫管理テンプレート",
            "倉庫の入庫・出庫・棚卸し・発注など、在庫管理業務を一通り含む業務テンプレート。複数倉庫や商品マスタの扱いの参考に。初期ユーザーは admin/admin。");

        public static ProjectCatalogEntry Sfa() => Make(
            "SFA", "SFATemplate.bin", "sfa_v2.db",
            "営業支援 (SFA) テンプレート",
            "顧客 / 商談 / 活動履歴 / 案件パイプラインなど、営業支援 (SFA) 業務を一通り含む業務テンプレート。営業案件の進捗管理の参考に。初期ユーザーは admin/admin。");

        public static ProjectCatalogEntry ProjectManagement() => Make(
            "ProjectManagement", "ProjectManagementTemplate.bin", "project_management_v2.db",
            "プロジェクト管理テンプレート",
            "プロジェクト / タスク / 工数 / 進捗管理など、プロジェクト管理業務を一通り含む業務テンプレート。タスク階層やガントチャート的な可視化の参考に。初期ユーザーは admin/admin。");

        public static List<ProjectCatalogEntry> All() =>
        [
            Empty(),
            GettingStarted(),
            PatternShowcase(),
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
            // headless CLI (template-create --data-dir) のサンプル DB 配置。UI なし・失敗は例外。既存ファイルは上書きしない
            ExtractSampleData = dataDir => ExtractSampleDbTo(Path.Combine(dataDir, sampleDbResource), sampleDbResource),
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
            //展開後、設計 JSON を保存経路と同じシリアライザで正規化する。
            //テンプレート作成後に増えた既定値プロパティを先出しし、「開いて保存しただけでメンバーが増える」差分を防ぐ。
            DesignJsonNormalizer.Normalize(path);
        }

        // サンプル DB を dbPath に展開する (無い / 0 byte のときだけ)。UI を出さない版
        static void ExtractSampleDbTo(string dbPath, string resourceName)
        {
            if (File.Exists(dbPath) && new FileInfo(dbPath).Length > 0) return;
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            using var stream = LoadResource(resourceName);
            using var file = File.Create(dbPath);
            stream.CopyTo(file);
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
            try
            {
                ExtractSampleDbTo(dbPath, resourceName);
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

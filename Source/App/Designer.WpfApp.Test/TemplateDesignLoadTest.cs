using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using NUnit.Framework;
using System.IO;
using System.Text;

namespace Designer.WpfApp.Test
{
    /// <summary>
    /// テンプレートマスタ (Source/TestData の8プロジェクト = 配布 bin の元) の全デザインファイルが
    /// 現在のシリアライザ設定で読み込めることを保証する。
    /// 合成ミニデータのテストでは拾えない「実データにしか無いパターン」(未知enum名等) による
    /// デシリアライズ互換性のデグレを検出する (tfs 側の TestDataDesignLoadTest と同型。
    /// マスタは 2026-07-30 に本体リポジトリからこちらへ移設された)。
    /// </summary>
    [TestFixture]
    public class TemplateDesignLoadTest
    {
        static readonly string[] TargetProjects =
        [
            "EmptyTemplate",
            "EmptyAuthTemplate",
            "GettingStartedTemplate",
            "InventoryManagementTemplate",
            "PatternShowcase",
            "PatternShowcaseAuth",
            "ProjectManagementTemplate",
            "SFATemplate",
        ];

        [OneTimeSetUp]
        public void LoadFieldAssemblies()
        {
            //Extras/ApexCharts のフィールド型 (TypeFullName) を TypeFinder が解決できるよう確実にロードする
            typeof(Codeer.LowCode.Bindings.ApexCharts.ApexChartsClientInitializer).ToString();
            typeof(Codeer.LowCode.Blazor.Extras.ExtrasClientInitializer).ToString();
        }

        [Test]
        public void テンプレートマスタの全デザインファイルが読み込める()
        {
            var testData = FindTestDataRoot();
            var failures = new StringBuilder();
            var moduleCount = 0;
            var pageFrameCount = 0;

            foreach (var project in TargetProjects)
            {
                var root = Path.Combine(testData, project);
                Assert.That(Directory.Exists(root), $"プロジェクトが見つからない(リネーム時はこのテストも更新): {root}");

                var moduleFiles = Directory.EnumerateFiles(root, "*.mod.json", SearchOption.AllDirectories).ToList();
                var frameFiles = Directory.EnumerateFiles(root, "*.frm.json", SearchOption.AllDirectories).ToList();
                Assert.That(moduleFiles.Count, Is.GreaterThan(0), $"モジュールが1つも無い: {project}");

                foreach (var file in moduleFiles)
                {
                    moduleCount++;
                    if (JsonConverterEx.DeserializeObject<ModuleDesign>(File.ReadAllText(file)) == null)
                    {
                        failures.AppendLine($"Module: {Path.GetRelativePath(testData, file)}");
                    }
                }
                foreach (var file in frameFiles)
                {
                    pageFrameCount++;
                    if (JsonConverterEx.DeserializeObject<PageFrameDesign>(File.ReadAllText(file)) == null)
                    {
                        failures.AppendLine($"PageFrame: {Path.GetRelativePath(testData, file)}");
                    }
                }
            }

            TestContext.Out.WriteLine($"checked modules={moduleCount} pageFrames={pageFrameCount}");
            //対象が空回りしていないこと(パス解決ミスやプロジェクト構成変更の検知)
            Assert.That(moduleCount, Is.GreaterThan(100), "対象モジュール数が少なすぎる(スキャンが空回りしていないか)");
            Assert.That(pageFrameCount, Is.GreaterThan(8), "対象PageFrame数が少なすぎる(スキャンが空回りしていないか)");

            Assert.That(failures.Length, Is.EqualTo(0), "読み込みに失敗したデザインファイル:\n" + failures);
        }

        static string FindTestDataRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "TestData");
                if (Directory.Exists(Path.Combine(candidate, "EmptyTemplate"))) return candidate;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Source/TestData が見つかりません");
        }
    }
}

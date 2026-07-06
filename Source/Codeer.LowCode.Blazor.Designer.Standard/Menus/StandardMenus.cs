using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.Designer.Extensibility.Views;
using Codeer.LowCode.Blazor.Designer.Standard.DbTableToModule;
using Codeer.LowCode.Blazor.Designer.Standard.ExcelCheatSheet;
using Codeer.LowCode.Blazor.Designer.Standard.ModuleToClass;
using Codeer.LowCode.Blazor.Designer.Standard.SeleniumPageObject;
using Codeer.LowCode.Blazor.Designer.Standard.Views;
using Codeer.LowCode.Blazor.Designer.Views.Windows;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Codeer.LowCode.Blazor.Designer.Standard
{
    /// <summary>
    /// デザイナ標準のツールメニュー。
    /// <see cref="AddAll"/> で全件登録できるほか、個別メソッドで必要なメニューだけ登録できる。
    /// いずれも MainWindow 生成後 (DesignerApp.OnStartup の base 呼び出し後) に呼ぶこと。
    /// </summary>
    public static class StandardMenus
    {
        /// <summary>標準メニューと DB 列変換 (PostgreSQL xmin) を全件登録する。</summary>
        public static void AddAll(DesignerEnvironment env)
        {
            AddImportModulesFromDatabase(env);
            AddExportPageObject(env);
            AddCreateDdl(env);
            AddCreateFieldClass(env);
            AddCreateFieldDataClass(env);
            AddExportExcelPrintCheatSheet(env);
            AddDbColumnTransformDefaults(env);
        }

        /// <summary>Tools &gt; Import Modules from Database。DB スキーマからモジュールを生成する。</summary>
        public static void AddImportModulesFromDatabase(DesignerEnvironment env)
            => env.AddMainMenu(() => ImportModulesFromDbTables(env), "Tools", "Import Modules from Database");

        /// <summary>Tools &gt; Export PageObject。Selenium テスト用 PageObject を出力する。</summary>
        public static void AddExportPageObject(DesignerEnvironment env)
            => env.AddMainMenu(() => ExportPageObject(env), "Tools", "Export PageObject");

        /// <summary>モジュール右クリック &gt; Create DDL。</summary>
        public static void AddCreateDdl(DesignerEnvironment env)
            => env.AddSolutionExplorerMenu(e => CreateDdl(env, e), SolutionExplorerMenuTarget.Module, "Create DDL");

        /// <summary>モジュール右クリック &gt; Create Field Class。</summary>
        public static void AddCreateFieldClass(DesignerEnvironment env)
            => env.AddSolutionExplorerMenu(e => ShowModuleClass(env, e, ClassGenerator.ModuleDesignToFieldClass, "Module to Field Class"),
                SolutionExplorerMenuTarget.Module, "Create Field Class");

        /// <summary>モジュール右クリック &gt; Create FieldData Class。</summary>
        public static void AddCreateFieldDataClass(DesignerEnvironment env)
            => env.AddSolutionExplorerMenu(e => ShowModuleClass(env, e, ClassGenerator.ModuleDesignToDataFieldClass, "Module to Field Data Class"),
                SolutionExplorerMenuTarget.Module, "Create FieldData Class");

        /// <summary>モジュール右クリック &gt; Export Excel Print CheatSheet。</summary>
        public static void AddExportExcelPrintCheatSheet(DesignerEnvironment env)
            => env.AddSolutionExplorerMenu(e => ExportExcelPrintCheatSheet(env, e), SolutionExplorerMenuTarget.Module, "Export Excel Print CheatSheet");

        /// <summary>
        /// DB からのモジュール生成時の標準列変換を登録する (PostgreSQL の xmin 行バージョン列を
        /// OptimisticLockingField にマップ)。
        /// </summary>
        public static void AddDbColumnTransformDefaults(DesignerEnvironment env)
            => env.AddDbColumnTransformHandler((dataSource, table, columnName) =>
            {
                var col = table.Columns.FirstOrDefault(e => e.Name == columnName);
                if (col == null) return null;
                if (dataSource.DataSourceType == DataSourceType.PostgreSQL && col.Name == "xmin")
                    return new OptimisticLockingFieldDesign { Name = SystemFieldNames.OptimisticLocking, DbColumn = columnName };
                return null;
            });

        static void ImportModulesFromDbTables(DesignerEnvironment env)
        {
            if (string.IsNullOrEmpty(env.CurrentFileDirectory)) return;

            //ユーザの選択をゲットする
            var datasourceToTables = env.GetDesignerSettings().DataSources.ToDictionary(e => e.Name, e => env.GetDbInfo(e.Name));
            var userSelected = DbTableSelectWindow.ShowDialog(datasourceToTables);
            if (userSelected == null) return;

            //モジュールに変換
            var err = DbTableParser.Import(env, userSelected.Value.selectedDataSource, userSelected.Value.selectedTables);
            if (!string.IsNullOrEmpty(err)) env.ShowToast(err, false);
        }

        static void ExportPageObject(DesignerEnvironment env)
        {
            if (string.IsNullOrEmpty(env.CurrentFileDirectory)) return;

            var nameInputDialog = new NameInputDialog
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (nameInputDialog.ShowDialog() != true) return;

            var ns = nameInputDialog.NameText;

            var folderDialog = new OpenFolderDialog();
            if (folderDialog.ShowDialog() != true) return;

            new SeleniumPageObjectBuilder
            {
                TargetPath = folderDialog.FolderName,
                Namespace = ns,
            }.Build(env.GetDesignData());

            env.ShowToast("PageObject exported", true);
        }

        static void CreateDdl(DesignerEnvironment env, SolutionExplorerMenuClickEventArgs e)
        {
            var mod = FindModule(env, e);
            if (mod == null) return;

            var dataSource = env.GetDesignerSettings().DataSources.FirstOrDefault(d => d.Name == mod.DataSourceName);
            if (dataSource == null)
            {
                env.ShowToast("Invalid Data Source", false);
                return;
            }

            var ddl = mod.CreateDDL(dataSource.DataSourceType, env.GetDbInfo(dataSource.Name));

            new DDLWindow
            {
                DesignerEnvironment = env,
                DataSource = dataSource,
                DisplayText = string.Join(Environment.NewLine, ddl),
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Title = "DDL",
            }.Show();
        }

        static void ShowModuleClass(DesignerEnvironment env, SolutionExplorerMenuClickEventArgs e,
            Func<ModuleDesign, string> generate, string title)
        {
            var mod = FindModule(env, e);
            if (mod == null) return;

            new TextDisplayWindow
            {
                DisplayText = generate(mod),
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Title = title,
            }.Show();
        }

        static void ExportExcelPrintCheatSheet(DesignerEnvironment env, SolutionExplorerMenuClickEventArgs e)
        {
            if (string.IsNullOrEmpty(env.CurrentFileDirectory)) return;

            var designData = env.GetDesignData();
            var module = FindModule(env, e);
            if (module == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Excel files (*.xlsx)|*.xlsx",
                FileName = $"{module.Name}_PrintExcelCheatSheet.xlsx"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var moduleToCheatSheet = new ModuleToExcelCheatSheet();
                var stream = moduleToCheatSheet.CreatePrintExcelCheatSheet(designData, module);
                File.WriteAllBytes(dialog.FileName, stream.ToArray());
                Process.Start(new ProcessStartInfo
                {
                    FileName = dialog.FileName,
                    UseShellExecute = true
                });
                env.ShowToast("Print Excel CheatSheet exported.", true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        static ModuleDesign? FindModule(DesignerEnvironment env, SolutionExplorerMenuClickEventArgs e)
        {
            var modName = e.Item.Split(".").First();
            var mod = env.GetDesignData().Modules.Find(modName);
            if (mod == null) env.ShowToast("Module not found", false);
            return mod;
        }
    }
}

using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using Codeer.LowCode.Blazor.DesignLogic;

namespace Codeer.LowCode.Blazor.Designer.Standard.DbTableToModule
{
    public static class DbTableParser
    {
        public static string Import(DesignerEnvironment designerEnvironment, string dataSourceName, List<DbTableDefinition> tables)
        {
            var modules = new List<string>();
            var err = new List<string>();
            var dataSource = designerEnvironment.GetDesignerSettings().DataSources
                .FirstOrDefault(e => e.Name == dataSourceName);
            foreach (var table in tables)
            {
                var module = new ModuleDesign()
                {
                    Name = DbNameToDesignName(table.Name),
                    DataSourceName = dataSourceName,
                    DbTable = table.Name
                };
                foreach (var col in table.Columns)
                {
                    // 列→フィールドの変換・命名・システム予約名の正規化はツールボックスの
                    // DB列ドロップと共通 (DbColumnFieldConverter。カスタム列変換フック込み)
                    module.Fields.Add(DbColumnFieldConverter.ConvertForModule(module, dataSource, table, col));
                }
                module.CreateLayouts();

                try
                {
                    File.WriteAllTextAsync(Path.Combine(designerEnvironment.CurrentFileDirectory, "Modules", $"{module.Name}.mod.json"), JsonConverterEx.SerializeObject(module));
                    modules.Add(module.Name);
                }
                catch (Exception exp)
                {
                    err.Add(exp.Message);
                }
            }

            //ページフレームに追加
            var designData = designerEnvironment.GetDesignData();
            var pageFrame = designData.PageFrames.Find("Main");
            if (pageFrame == null) pageFrame = designData.PageFrames.ToList().FirstOrDefault();
            if (pageFrame == null) return string.Empty;
            foreach (var module in modules)
            {
                if (pageFrame.Left.Links.Any(e => e.Module == module)) continue;
                pageFrame.Left.Links.Add(new PageLink
                {
                    Module = module,
                    Title = module,
                });
            }
            var path = Path.Combine(designerEnvironment.CurrentFileDirectory, "PageFrames", $"{pageFrame.Name}.frm.json");
            try
            {
                File.WriteAllText(path, JsonConverterEx.SerializeObject(pageFrame));
            }
            catch (Exception exp)
            {
                err.Add(exp.Message);
            }
            return string.Join(Environment.NewLine, err);
        }

        static string DbNameToDesignName(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;

            var words = source.Split(['_', '.']);
            if (words.Length == 1)
            {
                if (string.IsNullOrEmpty(words[0])) return string.Empty;
                var first = CultureInfo.InvariantCulture.TextInfo.ToUpper(words[0].Substring(0, 1));
                if (words[0].Length == 1) return first;
                return first + words[0].Substring(1);
            }

            var capitalizedWords = words.Select(word =>
                CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word.ToLower()));
            return string.Concat(capitalizedWords);
        }

    }
}

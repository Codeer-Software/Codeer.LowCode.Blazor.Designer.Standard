using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Design;
using Microsoft.CodeAnalysis.CSharp;
using System.Security;
using System.Text;

namespace Codeer.LowCode.Blazor.Designer.Standard.ModuleToClass
{
    /// <summary>
    /// DesignToDataFieldClasses の結果。Code = 生成した C#、Modules = クラスを出したモジュール名、Warnings = 出さなかったものとその理由。
    /// </summary>
    public class ClassGenerationResult
    {
        public string Code { get; init; } = string.Empty;
        public List<string> Modules { get; init; } = new();
        public List<string> Warnings { get; init; } = new();
    }

    public static class ClassGenerator
    {
        const string NewLine = "\r\n";

        sealed record PropertyInfo(string Name, Type DataType, string Summary);
        sealed record ClassInfo(string Name, List<PropertyInfo> Properties);

        public static string ModuleDesignToDataFieldClass(ModuleDesign mod)
            => GenerateModuleClass(mod, field => field.CreateData()?.GetType());

        public static string ModuleDesignToFieldClass(ModuleDesign mod)
            => GenerateModuleClass(mod, field => field.CreateField()?.GetType());

        /// <summary>
        /// デザインの全モジュールの FieldData 型のクラス (クラス名 = モジュール名、プロパティ名 = フィールド名) を 1 ファイルの C# にする (poco コマンド)。
        /// ModuleData.MapTo / Create・SearchCondition&lt;T&gt; 等で使う。リンク越しのプロパティは出さない (クラスは partial。使う側が別ファイルで足す)。
        /// </summary>
        public static ClassGenerationResult DesignToDataFieldClasses(DesignData design, string @namespace)
        {
            var warnings = new List<string>();
            var classes = new List<ClassInfo>();

            foreach (var module in design.Modules.ToList().OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                if (!IsValidIdentifier(module.Name))
                {
                    warnings.Add($"{module.Name}: モジュール名が C# の識別子として使えないためクラスを出していません");
                    continue;
                }

                var properties = new List<PropertyInfo>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (field, dataType) in EnumerateFieldTypes(module, field => CreateDataType(field, module.Name, warnings)))
                {
                    var position = $"{module.Name}.{field.Name}";
                    if (!IsValidIdentifier(field.Name))
                    {
                        warnings.Add($"{position}: フィールド名が C# の識別子として使えないためプロパティを出していません");
                        continue;
                    }
                    if (field.Name == module.Name)
                    {
                        warnings.Add($"{position}: フィールド名がモジュール名と同じためプロパティを出していません (C# ではクラス名と同じ名前のメンバーを持てない)");
                        continue;
                    }
                    if (!names.Add(field.Name))
                    {
                        warnings.Add($"{position}: 同じ名前のフィールドが既にあるためプロパティを出していません");
                        continue;
                    }
                    if (dataType.IsGenericType)
                    {
                        warnings.Add($"{position}: データの型 {dataType.FullName} がジェネリック型のためプロパティを出していません");
                        continue;
                    }
                    properties.Add(new PropertyInfo(field.Name, dataType, GetSummary(field)));
                }

                if (properties.Count == 0)
                {
                    warnings.Add($"{module.Name}: データを持つフィールドが無いためクラスを出していません");
                    continue;
                }
                classes.Add(new ClassInfo(module.Name, properties));
            }

            return new ClassGenerationResult
            {
                Code = BuildCode(classes, @namespace),
                Modules = classes.Select(e => e.Name).ToList(),
                Warnings = warnings,
            };
        }

        /// <summary>名前空間として使えるか (. 区切りの各部分が予約語でない C# の識別子)。</summary>
        public static bool IsValidNamespace(string @namespace)
            => @namespace.Split('.').All(e => IsValidIdentifier(e) && SyntaxFacts.GetKeywordKind(e) == SyntaxKind.None);

        static string GenerateModuleClass(ModuleDesign mod, Func<FieldDesignBase, Type?> resolveType)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine($"public class {mod.Name}");
            stringBuilder.AppendLine("{");
            foreach (var (field, type) in EnumerateFieldTypes(mod, resolveType))
                stringBuilder.AppendLine($"    public {type.Name}? {field.Name} {{ get; set; }}");
            stringBuilder.AppendLine("}");

            return stringBuilder.ToString();
        }

        // プロパティにするフィールドと型 (フィールド順。リンク越しと型の無いフィールドは除く)
        static IEnumerable<(FieldDesignBase Field, Type Type)> EnumerateFieldTypes(ModuleDesign mod, Func<FieldDesignBase, Type?> resolveType)
        {
            foreach (var field in mod.Fields)
            {
                if (IsLinkField(field.Name)) continue;

                var type = resolveType(field);
                if (type == null) continue;

                yield return (field, type);
            }
        }

        static Type? CreateDataType(FieldDesignBase field, string moduleName, List<string> warnings)
        {
            try
            {
                return field.CreateData()?.GetType();
            }
            catch (Exception ex)
            {
                warnings.Add($"{moduleName}.{field.Name}: データの型を取得できないためプロパティを出していません ({ex.Message})");
                return null;
            }
        }

        static string GetSummary(FieldDesignBase field)
        {
            var designTypeName = field.GetType().Name;
            if (designTypeName.EndsWith("Design", StringComparison.Ordinal)) designTypeName = designTypeName[..^"Design".Length];
            var displayName = (field as IDisplayName)?.DisplayName;
            return string.IsNullOrEmpty(displayName) ? designTypeName : $"{displayName} ({designTypeName})";
        }

        static string BuildCode(List<ClassInfo> classes, string @namespace)
        {
            // 単純名が別の型・生成するクラスと衝突する型と入れ子の型は完全修飾で書く
            var types = classes.SelectMany(e => e.Properties).Select(e => e.DataType).Distinct().ToList();
            var classNames = new HashSet<string>(classes.Select(e => e.Name), StringComparer.Ordinal);
            var ambiguous = new HashSet<string>(types.GroupBy(e => e.Name).Where(e => e.Count() > 1).Select(e => e.Key), StringComparer.Ordinal);
            bool useSimpleName(Type t) => !t.IsNested && !ambiguous.Contains(t.Name) && !classNames.Contains(t.Name);
            string typeName(Type t) => useSimpleName(t) ? t.Name : "global::" + t.FullName!.Replace('+', '.');

            var usings = types.Where(useSimpleName)
                .Select(e => e.Namespace)
                .Where(e => !string.IsNullOrEmpty(e) && e != @namespace)
                .Distinct()
                .OrderBy(e => e, StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            void line(string text = "") => sb.Append(text).Append(NewLine);

            line("// <auto-generated>");
            line("// デザイナの poco コマンドでデザインから生成したファイル。手で編集しない。");
            line("// デザインを変えたら poco コマンドで生成し直す。");
            line("// </auto-generated>");
            line("#nullable enable");
            line();
            foreach (var ns in usings) line($"using {ns};");
            if (usings.Count > 0) line();
            line($"namespace {@namespace}");
            line("{");
            for (var i = 0; i < classes.Count; i++)
            {
                if (i > 0) line();
                var cls = classes[i];
                line($"    public partial class {EscapeIdentifier(cls.Name)}");
                line("    {");
                foreach (var prop in cls.Properties)
                {
                    line($"        /// <summary>{SecurityElement.Escape(prop.Summary)}</summary>");
                    line($"        public {typeName(prop.DataType)}? {EscapeIdentifier(prop.Name)} {{ get; set; }}");
                }
                line("    }");
            }
            line("}");
            return sb.ToString();
        }

        private static bool IsLinkField(string fieldName)
        {
            var nameObj = new FieldName(fieldName);
            return nameObj.IsLink;
        }

        // 予約語でも @ を付ければ識別子として使える
        static bool IsValidIdentifier(string name)
            => !string.IsNullOrEmpty(name) && SyntaxFacts.IsValidIdentifier(name);

        static string EscapeIdentifier(string name)
            => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
    }
}

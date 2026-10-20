using Codeer.LowCode.Blazor.Designer.Extensibility;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Designer.Standard.ModuleToClass
{
    /// <summary>
    /// poco:
    ///   <designer.exe> poco "<projectDir>" --namespace <ns> --out-file "<csPath>" [--out "<resultJsonPath>"]
    ///   全モジュールの POCO (クラス名 = モジュール名、プロパティ名 = フィールド名、型 = FieldData 型) の C# を --out-file に書く
    ///   (GUI のモジュール右クリック > Create FieldData Class と同じ生成器)。
    ///   ModuleData.MapTo / Create・SearchCondition&lt;T&gt;・AggregateCondition&lt;T&gt; で使う。リンク越し ([Link]) のプロパティは出さない (クラスは partial)。
    ///   --out には { modules, warnings, outFile } (出さなかったモジュール・フィールドは warnings)。終了コード: 0 = 成功 / 2 = 失敗。
    /// </summary>
    public static class PocoCli
    {
        public const string Verb = "poco";

        public static void RegisterCli() => HeadlessCliVerbs.Register(Verb, RunCli);

        static int RunCli(string[] args)
        {
            var projectDir = args.Length > 1 ? args[1] : string.Empty;
            string? outPath = null, outFile = null, ns = null;
            for (var i = 2; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out" when i + 1 < args.Length: outPath = args[++i]; break;
                    case "--out-file" when i + 1 < args.Length: outFile = args[++i]; break;
                    case "--namespace" when i + 1 < args.Length: ns = args[++i]; break;
                }
            }

            try
            {
                if (string.IsNullOrEmpty(ns))
                    return WriteJson(outPath, new { error = "--namespace is required" }, 2);
                if (!ClassGenerator.IsValidNamespace(ns))
                    return WriteJson(outPath, new { error = $"invalid namespace: {ns}" }, 2);
                if (string.IsNullOrEmpty(outFile))
                    return WriteJson(outPath, new { error = "--out-file is required" }, 2);
                var designData = HeadlessDesignProject.Load(projectDir, out var error);
                if (designData == null)
                    return WriteJson(outPath, new { error }, 2);

                var result = ClassGenerator.DesignToDataFieldClasses(designData, ns);

                outFile = Path.GetFullPath(outFile);
                Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
                File.WriteAllText(outFile, result.Code, new UTF8Encoding(true));

                return WriteJson(outPath, new
                {
                    verb = Verb,
                    modules = result.Modules,
                    warnings = result.Warnings,
                    outFile,
                }, 0);
            }
            catch (Exception ex)
            {
                return WriteJson(outPath, new { error = ex.ToString() }, 2);
            }
        }

        static readonly JsonSerializerOptions _jsonWrite = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        static int WriteJson(string? outPath, object payload, int exitCode)
        {
            var json = JsonSerializer.Serialize(payload, _jsonWrite);
            if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, json, new UTF8Encoding(false));
            else Console.Out.WriteLine(json);
            return exitCode;
        }
    }
}

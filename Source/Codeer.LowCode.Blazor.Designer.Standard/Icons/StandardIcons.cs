using Codeer.LowCode.Blazor.Designer.Models;
using System.IO;

namespace Codeer.LowCode.Blazor.Designer.Standard
{
    /// <summary>
    /// デザイナ標準のアイコン候補 (Bootstrap Icons)。
    /// <see cref="AddBootstrapIcons"/> で IconCandidate に登録するか、
    /// <see cref="BootstrapIcons"/> で一覧を取得して選別してから登録する。
    /// </summary>
    public static class StandardIcons
    {
        public static IReadOnlyList<string> BootstrapIcons()
        {
            using var stream = typeof(StandardIcons).Assembly.GetManifestResourceStream(
                "Codeer.LowCode.Blazor.Designer.Standard.Icons.bootstrap_icons.txt")
                ?? throw new InvalidOperationException("embedded resource not found: bootstrap_icons.txt");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd()
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .Order()
                .ToList();
        }

        public static void AddBootstrapIcons() => IconCandidate.Icons.AddRange(BootstrapIcons());
    }
}

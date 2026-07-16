using MahApps.Metro.Controls;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace Codeer.LowCode.Blazor.Designer.Standard.Views
{
    /// <summary>
    /// Claude Code Workspace 展開完了ダイアログ。デザイナ標準の <see cref="Views.Windows.MessageWindow"/> は
    /// 素テキストでハイパーリンクを出せないため、オンラインマニュアルへのアンカー付きで別に用意している。
    /// </summary>
    public partial class ClaudeCodeWorkspaceResultDialog : MetroWindow
    {
        //オンラインマニュアル (Designer.Standard リポジトリの日本語ドキュメント)
        const string ManualUrl =
            "https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Designer.Standard/blob/main/Docs/claude_code_designer.md";

        public ClaudeCodeWorkspaceResultDialog()
        {
            InitializeComponent();
            _manualLink.NavigateUri = new Uri(ManualUrl);
        }

        /// <summary>メッセージ本文＋マニュアルリンクを表示する。</summary>
        public static void Show(string message, string title = "Claude Code Workspace")
        {
            var dialog = new ClaudeCodeWorkspaceResultDialog
            {
                Title = title,
                Owner = Application.Current.MainWindow,
            };
            dialog._message.Text = message;
            dialog.ShowDialog();
            Application.Current.MainWindow?.Activate();
            Application.Current.MainWindow?.Focus();
        }

        void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true });
            e.Handled = true;
        }

        void OkClick(object sender, RoutedEventArgs e) => Close();
    }
}

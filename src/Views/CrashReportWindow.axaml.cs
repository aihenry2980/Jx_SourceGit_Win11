using System;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class CrashReportWindow : ChromelessWindow
    {
        public CrashReportWindow()
        {
            InitializeComponent();
        }

        public void SetSummary(string summary) => ReportText.Text = summary;

        private async void CopyReport(object sender, RoutedEventArgs e)
        {
            try
            {
                await App.CopyTextAsync(ReportText.Text);
            }
            catch (Exception ex)
            {
                App.LogException(ex);
            }
        }

        private void OpenLogs(object sender, RoutedEventArgs e) => Native.OS.OpenInFileManager(
            Models.CrashReport.GetPendingDirectory(Native.OS.BasicDirectories.CacheDir, ReportText.Text));

        private void CloseReport(object sender, RoutedEventArgs e) => Close();
    }
}

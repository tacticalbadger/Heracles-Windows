using System.Windows.Controls;
using Heracles.Model;

namespace Heracles;

/// <summary>
/// Shown once, after the original app's library has been brought across:
/// what came, that the old library is safe and where the copy of it is, and
/// each thing that had to be adjusted to fit.
/// </summary>
internal sealed class ImportSummaryWindow : Window
{
    public ImportSummaryWindow(ImportReport report)
    {
        Title = "Heracles";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 620;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(22, 20, 22, 20) };
        panel.Children.Add(new TextBlock
        {
            Text = ImportSummary.Headline,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12),
        });
        panel.Children.Add(Words(ImportSummary.Counts(report)).Id("ImportSummaryCounts").Named(ImportSummary.Counts(report)));
        panel.Children.Add(Words(ImportSummary.Kept));
        // A path is something to copy: it can be selected.
        panel.Children.Add(new TextBox
        {
            Text = report.BackupPath,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 12),
        }.Id("ImportSummaryBackup").Named("Where the copy of the previous library is"));

        if (report.Repairs.Length > 0)
        {
            panel.Children.Add(Words(ImportSummary.Adjusted));
            var lines = new StackPanel();
            for (var index = 0; index < report.Repairs.Length; index++)
            {
                // U+2022, a bullet, written by number.
                var line = (char)0x2022 + " " + report.Repairs[index];
                lines.Children.Add(new TextBlock
                {
                    Text = line,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 4),
                }.Id($"ImportSummaryRepair:{index + 1}").Named(report.Repairs[index]));
            }
            panel.Children.Add(new ScrollViewer
            {
                Content = lines,
                MaxHeight = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 12),
                Focusable = false,
            });
        }

        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right }
            .Id("ImportSummaryOk");
        ok.Click += (_, _) => Close();
        panel.Children.Add(ok);

        Content = new Landmark { Child = panel }.Id("ImportSummary").Named(ImportSummary.Headline);
    }

    private static TextBlock Words(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
    };
}

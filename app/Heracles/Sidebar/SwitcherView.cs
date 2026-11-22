using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Heracles.Model;
using Heracles.Workspaces;

namespace Heracles.Sidebar;

/// <summary>
/// Quick Open: type part of a note's name or text, press Enter to open it.
/// Ctrl+Enter opens it in a new pane. Escape, or a click outside, puts it away.
/// </summary>
internal sealed class SwitcherView : Border
{
    private readonly Workspace workspace;
    private readonly TextBox query;
    private readonly ListBox results;
    private readonly List<ListBoxItem> rows = [];
    private int highlighted;
    private bool filling;

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new LandmarkPeer(this);

    public SwitcherView(Workspace workspace)
    {
        this.workspace = workspace;
        Width = 520;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 60, 0, 0);
        CornerRadius = new CornerRadius(8);
        Ui.AsSurface(this);
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.35 };
        this.Id("Switcher");

        query = new TextBox { FontSize = 16, Margin = new Thickness(10), BorderThickness = new Thickness(0), Background = Brushes.Transparent }
            .Id("SwitcherQuery").Named("Find a note");
        query.TextChanged += (_, _) => Fill();
        query.PreviewKeyDown += OnKey;

        results = new ListBox { MaxHeight = 320, BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Ui.Hairline, Background = Brushes.Transparent }
            .Id("SwitcherResults").Named("Matching notes");
        ScrollViewer.SetHorizontalScrollBarVisibility(results, ScrollBarVisibility.Disabled);
        // The list's own selection is never left standing: the arrow keys move
        // a highlight of ours, and a row that is selected (by a click, or by a
        // screen reader) is opened at once. So a click on the highlighted row
        // opens it too.
        results.SelectionChanged += (_, _) =>
        {
            if (filling || results.SelectedItem is not ListBoxItem chosen) return;
            highlighted = Math.Max(0, rows.IndexOf(chosen));
            Open(Ui.ControlIsDown);
        };

        var panel = new DockPanel();
        DockPanel.SetDock(query, Dock.Top);
        panel.Children.Add(query);
        panel.Children.Add(results);
        Child = panel;
    }

    /// <summary>Start afresh: an empty box with the keyboard in it, and the recent notes.</summary>
    public void Begin()
    {
        query.Text = "";
        Fill();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => Keyboard.Focus(query));
    }

    private void Fill()
    {
        filling = true;
        try
        {
            results.Items.Clear();
            rows.Clear();
            var notes = workspace.SwitcherResults(query.Text);
            if (notes.Count == 0)
            {
                var none = Ui.Row(Ui.Line("No notes match", 0.6));
                none.IsEnabled = false;
                results.Items.Add(none);
                return;
            }
            foreach (var note in notes)
            {
                // Where the note lives, to tell two with the same name apart.
                var path = string.Join(" › ", workspace.Model.Library.Path(note.Id).SkipLast(1).Select(ancestor => ancestor.DisplayTitle()));
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 220 });
                row.Children.Add(NoteIcon.For(note, 13));
                var title = Ui.Line(note.DisplayTitle());
                Grid.SetColumn(title, 1);
                row.Children.Add(title);
                var where = Ui.Line(path, 0.6);
                where.FontSize = 11;
                where.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(where, 2);
                row.Children.Add(where);
                var item = Ui.Row(row, note.Id).Id("SwitcherResult:" + note.Id).Named(note.DisplayTitle());
                rows.Add(item);
                results.Items.Add(item);
            }
            highlighted = 0;
            Highlight();
        }
        finally { filling = false; }
    }

    private void Highlight()
    {
        for (var index = 0; index < rows.Count; index++)
        {
            rows[index].Background = index == highlighted ? Ui.Accent(0.22) : Brushes.Transparent;
        }
        if (highlighted < rows.Count) rows[highlighted].BringIntoView();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                Open(Ui.ControlIsDown);
                e.Handled = true;
                break;
            case Key.Escape:
                workspace.IsSwitcherShown = false;
                e.Handled = true;
                break;
        }
    }

    private void Move(int step)
    {
        if (rows.Count == 0) return;
        highlighted = Math.Clamp(highlighted + step, 0, rows.Count - 1);
        Highlight();
    }

    private void Open(bool inSplit)
    {
        if (highlighted >= rows.Count || rows[highlighted].Tag is not string noteId) return;
        workspace.Open(noteId, inSplit ? SplitTarget.Right : null);
        workspace.IsSwitcherShown = false;
    }
}

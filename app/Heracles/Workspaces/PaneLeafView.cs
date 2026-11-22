using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Heracles.Model;
using Heracles.Notes;
using Heracles.Sidebar;

namespace Heracles.Workspaces;

/// <summary>
/// One pane: a strip of tabs, and under it the note that is showing, or a
/// line saying none is. It is kept for as long as the pane exists and updated
/// in place, so what is being typed in it survives changes elsewhere.
/// </summary>
internal sealed class PaneLeafView : DockPanel
{
    private readonly AppModel model;
    private readonly Workspace workspace;
    private readonly string leafId;
    private readonly Border strip;
    private readonly StackPanel tabs;
    private readonly ContentControl content;
    private NoteDetailView? detail;
    private Point? pressedAt;

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new LandmarkPeer(this);

    public PaneLeafView(AppModel model, Workspace workspace, string leafId)
    {
        this.model = model;
        this.workspace = workspace;
        this.leafId = leafId;
        this.Id("Pane:" + leafId);

        tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 4) };
        strip = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            AllowDrop = true,
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = tabs,
            },
        };
        strip.BorderBrush = Ui.Hairline;
        // Dropped past the last tab: it goes to the end.
        strip.Drop += (_, e) => DropTab(e, int.MaxValue);
        strip.DragOver += AllowTabs;
        SetDock(strip, Dock.Top);
        Children.Add(strip);

        content = new ContentControl { Focusable = false };
        Children.Add(content);

        // A click anywhere in a pane makes it the one the next note opens in.
        PreviewMouseDown += (_, _) => workspace.Tree = workspace.Tree.Focus(leafId);
    }

    public void Update(PaneLeaf leaf)
    {
        var several = workspace.Tree.Leaves.Count > 1;
        var focused = workspace.Tree.FocusedLeafId == leafId;

        tabs.Children.Clear();
        for (var index = 0; index < leaf.Tabs.Length; index++)
        {
            if (model.Library.Note(leaf.Tabs[index]) is { } tabNote) tabs.Children.Add(Tab(leaf, tabNote, index, several));
        }
        strip.Visibility = leaf.Tabs.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        // The focused pane's strip is tinted when there is more than one pane.
        strip.Background = focused && several ? Ui.Accent(0.10) : Brushes.Transparent;

        if (leaf.ActiveTab is { } active && model.Library.Note(active) is { } note)
        {
            if (detail is null || detail.NoteId != note.Id)
            {
                detail = new NoteDetailView(model, workspace, leafId, note);
                content.Content = detail;
            }
            else
            {
                detail.Update(note);
                if (!ReferenceEquals(content.Content, detail)) content.Content = detail;
            }
        }
        else
        {
            detail = null;
            content.Content = Empty();
        }
    }

    private FrameworkElement Empty()
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.7 };
        panel.Children.Add(Ui.Glyph("", 36));
        panel.Children.Add(new TextBlock { Text = "No Note Open", FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 4) });
        panel.Children.Add(new TextBlock { Text = "Choose a note in the sidebar, or press Ctrl+K to find one.", HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        // The whole pane takes the click, not only the words.
        return new Landmark { Background = Brushes.Transparent, Child = panel }.Id("EmptyPane:" + leafId).Named("No Note Open");
    }

    private FrameworkElement Tab(PaneLeaf leaf, Note note, int index, bool several)
    {
        var isActive = leaf.ActiveTab == note.Id;
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = NoteIcon.For(note, 12);
        icon.Margin = new Thickness(0, 0, 6, 0);
        label.Children.Add(icon);
        var name = Ui.Line(note.DisplayTitle(), isActive ? 1 : 0.7);
        name.MaxWidth = 180;
        label.Children.Add(name);

        var open = Ui.QuietButton(label, () => workspace.Tree = workspace.Tree.Activate(note.Id, leafId), note.DisplayTitle().Length > 28 ? note.DisplayTitle()[..Math.Min(200, note.DisplayTitle().Length)] : null)
            .Id($"Tab:{leafId}:{note.Id}").Named(note.DisplayTitle());
        var close = Ui.QuietButton(Ui.Glyph("", 8), () => workspace.Tree = workspace.Tree.CloseTab(note.Id, leafId), "Close tab")
            .Id($"TabClose:{leafId}:{note.Id}").Named("Close " + note.DisplayTitle());
        close.Opacity = 0.6;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(open);
        row.Children.Add(close);
        var tab = new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4, 1, 2, 1),
            Margin = new Thickness(0, 0, 2, 0),
            Background = isActive ? Ui.Accent(0.18) : Brushes.Transparent,
            AllowDrop = true,
        };

        var menu = new ContextMenu();
        menu.Items.Add(Ui.Item("Split Right", () => workspace.Tree = workspace.Tree.Split(leafId, SplitTarget.Right, note.Id, out _)));
        menu.Items.Add(Ui.Item("Split Down", () => workspace.Tree = workspace.Tree.Split(leafId, SplitTarget.Down, note.Id, out _)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Ui.Item("Close Tab", () => workspace.Tree = workspace.Tree.CloseTab(note.Id, leafId)));
        menu.Items.Add(Ui.Item("Close Other Tabs", () =>
        {
            var tree = workspace.Tree;
            foreach (var other in leaf.Tabs.Where(other => other != note.Id)) tree = tree.CloseTab(other, leafId);
            workspace.Tree = tree;
        }));
        if (several) menu.Items.Add(Ui.Item("Close Pane", () => workspace.Tree = workspace.Tree.CloseLeaf(leafId)));
        tab.ContextMenu = menu;

        // A tab is picked up once the mouse has moved a little with the button down.
        open.PreviewMouseLeftButtonDown += (_, e) => pressedAt = e.GetPosition(this);
        open.PreviewMouseLeftButtonUp += (_, _) => pressedAt = null;
        open.PreviewMouseMove += (_, e) =>
        {
            if (pressedAt is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var moved = e.GetPosition(this) - start;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            pressedAt = null;
            DragDrop.DoDragDrop(open, new DataObject(DataFormats.UnicodeText, new TabDrag(leafId, note.Id).Text), DragDropEffects.Move);
        };
        tab.DragOver += AllowTabs;
        tab.Drop += (_, e) => DropTab(e, index);
        return tab;
    }

    private static TabDrag? Dragged(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.UnicodeText) ? TabDrag.Parse(e.Data.GetData(DataFormats.UnicodeText) as string) : null;

    private static void AllowTabs(object sender, DragEventArgs e)
    {
        e.Effects = Dragged(e) is null ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
    }

    private void DropTab(DragEventArgs e, int index)
    {
        if (Dragged(e) is not { } drag) return;
        workspace.Tree = workspace.Tree.MoveTab(drag.NoteId, drag.LeafId, leafId, index);
        e.Handled = true;
    }
}

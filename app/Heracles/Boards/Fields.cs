using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Heracles.Boards;

/// <summary>A text box that says what it is for while it is empty.</summary>
internal class HintedBox : Grid
{
    private readonly TextBlock hint;

    /// <summary>Set while the box is filled from what is stored, so that is not taken for typing.</summary>
    protected bool Updating { get; private set; }

    public TextBox Box { get; }

    public HintedBox(string hintText, string id, bool quiet = false)
    {
        Box = new TextBox { VerticalContentAlignment = VerticalAlignment.Center }.Id(id).Named(hintText);
        if (quiet)
        {
            // No frame until it is typed in: it reads as part of what it sits in.
            Box.BorderThickness = new Thickness(0);
            Box.Background = Brushes.Transparent;
            Box.Padding = new Thickness(2, 2, 2, 2);
            Box.MinHeight = 0;
        }
        hint = Ui.Line(hintText, 0.5);
        hint.IsHitTestVisible = false;
        hint.Margin = quiet ? new Thickness(5, 0, 0, 0) : new Thickness(11, 0, 0, 0);
        Box.TextChanged += (_, _) => hint.Visibility = Box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Children.Add(Box);
        Children.Add(hint);
    }

    /// <summary>Put text in the box that nobody typed.</summary>
    public void Show(string text)
    {
        if (Box.Text == text) return;
        Updating = true;
        try { Box.Text = text; }
        finally { Updating = false; }
    }
}

/// <summary>
/// A box for something new (a column, a card, a label, a subtask, a
/// comment). What is typed is handed over on Enter, or by the button that
/// appears at its end once there is something to hand over, and the box is
/// emptied for the next.
///
/// The button is there for the mouse, and for whatever fills the box
/// without a keyboard (a screen reader's "set value", an automated check):
/// nothing then presses Enter. It is found as <c>Add:&lt;the box's id&gt;</c>.
/// </summary>
internal sealed class EntryBox : HintedBox
{
    private const double ButtonRoom = 26;

    private readonly Action<string> commit;

    public EntryBox(string hintText, string id, Action<string> commit, bool quiet = false) : base(hintText, id, quiet)
    {
        this.commit = commit;
        Box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Commit();
            e.Handled = true;
        };

        // It does not take the keyboard: after a click the box still has it, for the next one.
        var add = Ui.QuietButton(Ui.Glyph(((char)0xE710).ToString(), 10), Commit, "Add").Id("Add:" + id).Named("Add");
        add.HorizontalAlignment = HorizontalAlignment.Right;
        add.Margin = new Thickness(0, 0, 3, 0);
        add.Visibility = Visibility.Collapsed;
        Children.Add(add);
        var padding = Box.Padding;
        Box.TextChanged += (_, _) =>
        {
            var hasText = Box.Text.Length > 0;
            add.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
            Box.Padding = hasText ? new Thickness(padding.Left, padding.Top, padding.Right + ButtonRoom, padding.Bottom) : padding;
        };
    }

    private void Commit()
    {
        var text = Box.Text;
        Show("");
        commit(text);
    }
}

/// <summary>
/// A box that holds something stored (a title, a place, a column's name).
/// What is typed is kept on Enter and when the keyboard leaves the box.
/// </summary>
internal sealed class StoredBox : HintedBox
{
    private readonly Action<string> commit;

    public StoredBox(string hintText, string id, Action<string> commit, bool quiet = false) : base(hintText, id, quiet)
    {
        this.commit = commit;
        Box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            commit(Box.Text);
            e.Handled = true;
        };
        Box.LostKeyboardFocus += (_, _) => commit(Box.Text);
        Box.TextChanged += (_, _) =>
        {
            if (!Updating && !Box.IsKeyboardFocused) commit(Box.Text);
        };
    }

    /// <summary>Show what is stored. What is being typed is not replaced by it.</summary>
    public void ShowStored(string text)
    {
        if (!Box.IsKeyboardFocused) Show(text);
    }

    /// <summary>Keep what the box holds now: the view is going.</summary>
    public void Commit() => commit(Box.Text);
}

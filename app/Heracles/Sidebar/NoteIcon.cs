namespace Heracles.Sidebar;

/// <summary>
/// The picture beside a note's name: its own icon, an emoji drawn in colour,
/// or a symbol for the kind of note it is.
/// </summary>
internal static class NoteIcon
{
    public static FrameworkElement For(Note note, double size) =>
        string.IsNullOrWhiteSpace(note.Icon) ? Ui.Glyph(Symbol(note.Kind), size) : EmojiText(note.Icon, size);

    /// <summary>
    /// Emoji in colour. WPF draws them in one colour, so this goes through
    /// the Emoji.Wpf library, as everything that only shows emoji does.
    /// </summary>
    public static FrameworkElement EmojiText(string text, double size) => new global::Emoji.Wpf.TextBlock
    {
        Text = text,
        FontSize = size,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static string Symbol(NoteKind kind) => kind switch
    {
        NoteKind.Notebook => "", // Library
        NoteKind.Section => "", // Folder
        NoteKind.TodoList => "", // CheckList
        _ => "", // Document
    };
}

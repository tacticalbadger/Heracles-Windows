using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Heracles.Editor;

// What a screen reader, dictation, and the end-to-end checks see of the
// editor. The views are drawn by hand, and a view drawn by hand is nothing
// at all to UI Automation until it says what it is.

/// <summary>The page: a document, named by the note's words.</summary>
internal sealed class EditorPagePeer(BlockEditorView page) : FrameworkElementAutomationPeer(page)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    protected override string GetClassNameCore() => nameof(BlockEditorView);

    protected override string GetNameCore()
    {
        var words = page.Editor.PlainText;
        return words.Length == 0 ? "Empty note" : words;
    }

    protected override bool IsControlElementCore() => true;

    protected override bool IsContentElementCore() => true;
}

/// <summary>
/// A block with text: found as <c>Block:&lt;id&gt;</c>, named by its words,
/// with its kind as its status, and with a value that can be read and set.
/// </summary>
internal sealed class BlockTextPeer(TextHoldingView view) : FrameworkElementAutomationPeer(view), IValueProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;

    protected override string GetClassNameCore() => view.GetType().Name;

    protected override string GetAutomationIdCore() => "Block:" + view.Block.Id;

    protected override string GetNameCore() => view.ShownText;

    /// <summary>What kind of block it is, in a word or two: "heading 2", "todo done".</summary>
    protected override string GetItemStatusCore()
    {
        var block = view.Block;
        return block.Kind switch
        {
            BlockKind.Heading => $"heading {block.Level}",
            BlockKind.Todo => block.IsChecked ? "todo done" : "todo",
            BlockKind.Code => $"code {block.Language}",
            _ => block.Kind.ToString().ToLowerInvariant(),
        };
    }

    protected override bool IsControlElementCore() => true;

    protected override bool IsContentElementCore() => true;

    protected override bool IsKeyboardFocusableCore() => true;

    protected override bool IsEnabledCore() => true;

    protected override void SetFocusCore() =>
        view.Editor.Select(EditorSelection.Caret(view.Block.Id, view.Block.Text.Length));

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);

    public bool IsReadOnly => !view.Editor.IsEditable;

    public string Value => view.ShownText;

    public void SetValue(string value)
    {
        if (!view.Editor.ReplaceText(view.Block.Id, value ?? ""))
        {
            throw new InvalidOperationException("This note cannot be changed.");
        }
    }
}

using System.Windows.Controls;
using System.Windows.Input;

namespace Heracles.Editor;

// What the views already call and the next tasks of the plan build, each
// with its tests: typing and keys, then block actions. Until then each of
// these does nothing, and this file goes when the last of them is built.
internal sealed partial class BlockEditorController
{
    private void DocumentReplaced()
    {
    }

    private void SelectionMoved()
    {
    }

    private void BoxTextChanged(object sender, TextChangedEventArgs e)
    {
    }

    private void BoxSelectionChanged(object sender, RoutedEventArgs e)
    {
    }

    private bool HandleKey(BlockTextBox box, Key key, ModifierKeys modifiers) => false;

    private void UpdateTypingStyle(BlockTextBox box)
    {
    }

    internal void CloseSlashMenu()
    {
    }

    internal void PageDidLayout()
    {
    }

    internal void SelectBlocks(IReadOnlyList<string> blocks, bool extending)
    {
    }

    internal void SetChecked(string id, bool isChecked)
    {
    }

    internal void SetOpen(string id, bool open)
    {
    }

    internal void SetLanguage(string id, string language)
    {
    }

    internal void ChooseEmoji(string id, UIElement from)
    {
    }
}

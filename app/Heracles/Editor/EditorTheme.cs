using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Heracles.Editor;

/// <summary>The font a block's text is set in, before any marks.</summary>
internal readonly record struct EditorFont(FontFamily Family, double Size, FontWeight Weight);

/// <summary>
/// How the editor looks: fonts, colours and spacing, and the conversion
/// between a block's text and what a text box or static text shows.
///
/// Sizes are the Mac editor's, a point taken as a pixel. Colours are named
/// by the Fluent theme's own keys, so they follow light and dark; where the
/// theme is not loaded (the tests) the text simply keeps the colour it
/// inherits.
/// </summary>
internal static class EditorTheme
{
    public const double BodySize = 15;
    public const double CodeSize = 13;

    /// <summary>The widest a note's text column gets.</summary>
    public const double ContentWidth = 720;

    /// <summary>Room beside the text column, where the block handle appears.</summary>
    public const double Gutter = 60;

    /// <summary>The width of a list marker, and so how far nested blocks are indented.</summary>
    public const double Indent = 26;

    /// <summary>Space between one block and the next.</summary>
    public const double BlockSpacing = 5;

    public const double ColumnGap = 40;
    public const double TopPadding = 8;

    /// <summary>Empty space below the last block, to click in and to scroll into.</summary>
    public const double BottomPadding = 140;

    /// <summary>
    /// The room at each side of a block's text. A text box's document keeps
    /// this much whatever it is told, so static text is given the same: the
    /// two then break their lines alike and are the same height.
    /// </summary>
    public const double TextPadding = 5;

    // Fonts

    public static readonly FontFamily Prose = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New");

    public static EditorFont FontFor(Block block) => block.Kind switch
    {
        BlockKind.Heading => block.Level switch
        {
            1 => new EditorFont(Prose, 28, FontWeights.Bold),
            2 => new EditorFont(Prose, 22, FontWeights.Bold),
            3 => new EditorFont(Prose, 18, FontWeights.SemiBold),
            _ => new EditorFont(Prose, BodySize, FontWeights.SemiBold),
        },
        BlockKind.Code => new EditorFont(Mono, CodeSize, FontWeights.Normal),
        _ => new EditorFont(Prose, BodySize, FontWeights.Normal),
    };

    /// <summary>The height of every line of a block's text. Fixed, so that marks and emoji do not move the lines.</summary>
    public static double LineHeight(Block block)
    {
        var font = FontFor(block);
        var spacing = block.Kind == BlockKind.Code ? 3 : block.Kind == BlockKind.Heading ? 2 : 4;
        return Math.Round(font.Size * font.Family.LineSpacing) + spacing;
    }

    /// <summary>Extra space above a block, on top of the usual spacing.</summary>
    public static double TopMargin(Block block) => block.Kind != BlockKind.Heading ? 0 : block.Level switch
    {
        1 => 16,
        2 => 12,
        3 => 8,
        _ => 4,
    };

    // Colours, by the theme's names

    public const string PrimaryText = "TextFillColorPrimaryBrush";
    public const string SecondaryText = "TextFillColorSecondaryBrush";
    public const string TertiaryText = "TextFillColorTertiaryBrush";
    public const string LinkText = "AccentTextFillColorPrimaryBrush";
    public const string InlineCodeText = "SystemFillColorCriticalBrush";

    /// <summary>A faint wash that reads on light and on dark: behind inline code, and the panel of a callout or a code block.</summary>
    public static Brush Panel { get; } = Frozen(new SolidColorBrush(Colors.Gray) { Opacity = 0.12 });

    /// <summary>The theme's brush of that name where an element can find it, or the fallback.</summary>
    public static Brush Themed(FrameworkElement element, string key, Brush fallback) =>
        element.TryFindResource(key) as Brush ?? fallback;

    /// <summary>The colour text is drawn in where an element stands, thinned.</summary>
    public static Brush TextBrush(FrameworkElement element, double opacity = 1)
    {
        var inherited = TextElement.GetForeground(element);
        if (opacity >= 1) return inherited;
        var thinned = inherited.Clone();
        thinned.Opacity *= opacity;
        thinned.Freeze();
        return thinned;
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    // Text

    /// <summary>
    /// The marks on a run of text, as the model holds them. This is what the
    /// editor reads back from a text box. Fonts and colours are only how it
    /// looks.
    /// </summary>
    public static readonly DependencyProperty MarksProperty =
        DependencyProperty.RegisterAttached("Marks", typeof(TextStyle), typeof(EditorTheme));

    /// <summary>An inline node this editor does not know, on the run that stands in for it.</summary>
    public static readonly DependencyProperty AtomProperty =
        DependencyProperty.RegisterAttached("Atom", typeof(JsonValue), typeof(EditorTheme));

    private static readonly TextDecorationCollection Struck = TextDecorations.Strikethrough;
    private static readonly TextDecorationCollection Underlined = TextDecorations.Underline;

    private static readonly TextDecorationCollection StruckAndUnderlined = MakeStruckAndUnderlined();

    private static TextDecorationCollection MakeStruckAndUnderlined()
    {
        var both = new TextDecorationCollection(TextDecorations.Strikethrough);
        both.Add(TextDecorations.Underline);
        both.Freeze();
        return both;
    }

    /// <summary>Make an inline look as its marks say, in a block, and remember the marks on it.</summary>
    private static T Marked<T>(T inline, TextStyle style, Block block) where T : Inline
    {
        inline.SetValue(MarksProperty, style);
        if (block.Kind == BlockKind.Code) return inline;

        var isDone = block.Kind == BlockKind.Todo && block.IsChecked;
        if (style.Bold) inline.FontWeight = FontWeights.Bold;
        if (style.Italic) inline.FontStyle = FontStyles.Italic;
        if (isDone) inline.SetResourceReference(TextElement.ForegroundProperty, SecondaryText);
        if (style.Code)
        {
            inline.FontFamily = Mono;
            inline.FontSize = Math.Round(FontFor(block).Size * 0.9);
            inline.Background = Panel;
            inline.SetResourceReference(TextElement.ForegroundProperty, InlineCodeText);
        }
        if (style.Link is not null) inline.SetResourceReference(TextElement.ForegroundProperty, LinkText);

        var struck = style.Strike || isDone;
        var underlined = style.Link is not null;
        if (struck || underlined) inline.TextDecorations = struck && underlined ? StruckAndUnderlined : struck ? Struck : Underlined;
        return inline;
    }

    /// <summary>
    /// A block's text as inlines: runs, with an element of its own for each
    /// line break. With <paramref name="emoji"/>, each emoji is an element
    /// drawn in colour; that is for static text, since such an element is not
    /// text a text box can count.
    /// </summary>
    public static IEnumerable<Inline> Inlines(Block block, bool emoji)
    {
        foreach (var span in block.Text.Spans)
        {
            if (span.Atom is not null)
            {
                // An unknown inline node is shown as a small mark, one character wide.
                var mark = Marked(new Run(RichText.AtomCharacter), TextStyle.Plain, block);
                mark.SetValue(AtomProperty, span.Atom);
                mark.SetResourceReference(TextElement.ForegroundProperty, TertiaryText);
                yield return mark;
                continue;
            }
            var lines = span.Text.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (index > 0) yield return Marked(new LineBreak(), span.Style, block);
                var line = lines[index];
                if (line.Length == 0) continue;
                if (!emoji || block.Kind == BlockKind.Code || !MayHoldEmoji(line))
                {
                    yield return Marked(new Run(line), span.Style, block);
                    continue;
                }
                var at = 0;
                foreach (Match match in global::Emoji.Wpf.EmojiData.MatchOne.Matches(line))
                {
                    if (match.Index > at) yield return Marked(new Run(line[at..match.Index]), span.Style, block);
                    yield return ColourEmoji(match.Value, span.Style, block);
                    at = match.Index + match.Length;
                }
                if (at < line.Length) yield return Marked(new Run(line[at..]), span.Style, block);
            }
        }
    }

    /// <summary>
    /// An emoji as an element drawn in colour. Its own foreground is pinned,
    /// as the library pins it in its own text block: left to take the
    /// colour of the text around it, the emoji is drawn as a flat shape of
    /// that colour.
    /// </summary>
    private static Inline ColourEmoji(string emoji, TextStyle style, Block block)
    {
        var inline = new global::Emoji.Wpf.EmojiInline { Text = emoji, FontSize = FontFor(block).Size, Foreground = Brushes.Black };
        inline.SetValue(MarksProperty, style);
        return inline;
    }

    private static bool MayHoldEmoji(string text)
    {
        var starts = global::Emoji.Wpf.EmojiData.MatchStart;
        foreach (var character in text)
        {
            // Digits and the like begin keycap emoji and little else: not
            // worth looking for in every paragraph that has a number in it.
            if (character > 0xFF && starts.Contains(character)) return true;
        }
        return false;
    }

    /// <summary>
    /// Read a block's text back from the paragraph of a text box. Only the
    /// editor's own marks count: whatever fonts or colours arrived by some
    /// other road are not marks.
    /// </summary>
    public static RichText RichTextFrom(Paragraph paragraph, bool isCode = false)
    {
        var text = new RichText();
        foreach (var inline in Leaves(paragraph.Inlines))
        {
            var style = isCode ? TextStyle.Plain : inline.GetValue(MarksProperty) as TextStyle ?? TextStyle.Plain;
            switch (inline)
            {
                case LineBreak:
                    text += new RichText("\n", style);
                    break;
                case Run run when !isCode && run.GetValue(AtomProperty) is JsonValue atom:
                    // One stored node per object character.
                    foreach (var character in run.Text)
                    {
                        if (character == RichText.AtomCharacter[0]) text += RichText.FromInline([atom]);
                    }
                    break;
                case Run run:
                    // An object character without a node behind it is
                    // something from elsewhere. There is nothing to store for it.
                    text += new RichText(Clean(run.Text).Replace(RichText.AtomCharacter, ""), style);
                    break;
                case global::Emoji.Wpf.EmojiInline emoji:
                    text += new RichText(emoji.Text ?? "", style);
                    break;
            }
        }
        return text;
    }

    /// <summary>The inlines that are text, in order: what spans wrap is taken out of them.</summary>
    public static IEnumerable<Inline> Leaves(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Span span)
            {
                foreach (var inner in Leaves(span.Inlines)) yield return inner;
            }
            else
            {
                yield return inline;
            }
        }
    }

    /// <summary>Every kind of line break becomes the one the editor stores.</summary>
    public static string Clean(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Replace(LineSeparator, '\n').Replace(ParagraphSeparator, '\n');

    // The Unicode separators, by number: written in the source as
    // characters they would end the line they are on.
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    // Static text

    /// <summary>
    /// A block's text as it is drawn while nobody is typing in it. It breaks
    /// its lines where the block's text box does and is the same height.
    /// </summary>
    public static TextBlock StaticText(Block block)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Padding = new Thickness(TextPadding, 0, TextPadding, 0),
        };
        Fill(text, block);
        return text;
    }

    public static void Fill(TextBlock text, Block block)
    {
        var font = FontFor(block);
        text.FontFamily = font.Family;
        text.FontSize = font.Size;
        text.FontWeight = font.Weight;
        text.LineHeight = LineHeight(block);
        text.Inlines.Clear();
        text.Inlines.AddRange(Inlines(block, emoji: true));
    }
}

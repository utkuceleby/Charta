using AngleSharp.Dom;
using Charta;

namespace Charta.Html.Css;

internal enum DisplayKind
{
    Block,
    Inline,
    InlineBlock,
    ListItem,
    Flex,
    Table,
    TableRowGroup,
    TableRow,
    TableCell,
    None,
}

internal enum FlexDirection
{
    Row,
    Column,
}

internal enum WhiteSpaceKind
{
    Normal,
    Pre,
}

internal enum TextTransformKind
{
    None,
    Uppercase,
    Lowercase,
    Capitalize,
}

internal enum VerticalAlignKind
{
    Baseline,
    Super,
    Sub,
}

internal enum ListMarker
{
    Disc,
    Circle,
    Square,
    Decimal,
    None,
}

/// <summary>A fully resolved style for one element — the output of the cascade plus inheritance.</summary>
internal sealed class ComputedStyle
{
    public DisplayKind Display { get; set; } = DisplayKind.Inline;

    public double FontSize { get; set; } = 12;

    public string? FontFamily { get; set; }

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public bool Underline { get; set; }

    public bool LineThrough { get; set; }

    public Color Color { get; set; } = Charta.Color.Black;

    public TextAlign TextAlign { get; set; } = TextAlign.Left;

    public double LetterSpacing { get; set; }

    public double LineHeight { get; set; } = 1.2;

    public VerticalAlignKind VerticalAlign { get; set; } = VerticalAlignKind.Baseline;

    public Color? BackgroundColor { get; set; }

    public double? Width { get; set; }

    public double MarginTop { get; set; }

    public double MarginRight { get; set; }

    public double MarginBottom { get; set; }

    public double MarginLeft { get; set; }

    public double PaddingTop { get; set; }

    public double PaddingRight { get; set; }

    public double PaddingBottom { get; set; }

    public double PaddingLeft { get; set; }

    public double BorderThickness { get; set; }

    public Color BorderColor { get; set; } = Charta.Color.Black;

    public ListMarker ListStyleType { get; set; } = ListMarker.Disc;

    public WhiteSpaceKind WhiteSpace { get; set; } = WhiteSpaceKind.Normal;

    public TextTransformKind TextTransform { get; set; } = TextTransformKind.None;

    public FlexDirection FlexDirection { get; set; } = FlexDirection.Row;

    public double FlexGrow { get; set; }

    public double Gap { get; set; }

    /// <summary>A child inherits the typographic properties; box properties reset to their defaults.</summary>
    public ComputedStyle InheritForChild() => new()
    {
        FontSize = FontSize,
        FontFamily = FontFamily,
        Bold = Bold,
        Italic = Italic,
        Color = Color,
        TextAlign = TextAlign,
        LetterSpacing = LetterSpacing,
        LineHeight = LineHeight,
        ListStyleType = ListStyleType,
        WhiteSpace = WhiteSpace,
        TextTransform = TextTransform,
        // Display, flex, and box properties are not inherited.
    };
}

internal enum TextAlign
{
    Left,
    Center,
    Right,
    Justify,
}

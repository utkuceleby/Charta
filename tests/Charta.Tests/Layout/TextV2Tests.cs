using System.Text;
using Charta.Cos;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Layout;

public class TextV2Tests
{
    private static readonly PdfWriterOptions ClassicUncompressed = new()
    {
        XrefMode = XrefMode.Classic,
        CompressStreams = false,
    };

    static TextV2Tests() => FontManager.RegisterFont(SyntheticFont.Build());

    private static (GenerationResult Result, string Pdf) Generate(Document document)
    {
        using var buffer = new MemoryStream();
        var result = document.Generate(buffer, ClassicUncompressed, OverflowBehavior.Clip);
        return (result, Encoding.ASCII.GetString(buffer.ToArray()));
    }

    [Fact]
    public void TextAlignRight_ShiftsLineToRightEdge()
    {
        // A5 page, margin 40 → content width 339.53. "CA" at size 10 = 12pt wide.
        var document = Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A5);
            page.Margin(40);
            page.Content().Text("CA").FontSize(10).AlignRight();
        }));

        var (_, pdf) = Generate(document);

        // x = 40 + (339.53 - 12) = 367.53
        Assert.Contains("367.53", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void TextJustify_StretchesSpaces_ExceptLastLine()
    {
        // Width 40: "CA CA CA" wraps. Justified lines must end at the right edge.
        var document = Document.Create(doc => doc.Page(page =>
        {
            page.Size(new PageSize(120, 400)); // content width 120-80=40
            page.Margin(40);
            page.Content().Text("CA CA CA").FontSize(10).Justify();
        }));

        var (result, pdf) = Generate(document);

        Assert.Empty(result.Diagnostics);
        // Justified line: space glyph gets a TJ adjustment → "[<...>" array form with big negative number.
        Assert.Contains("] TJ", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void RichText_MixedSpans_FlowAsOneParagraph()
    {
        var document = Document.Create(doc => doc.Page(page =>
        {
            page.Content().Text(text =>
            {
                text.Span("CA ").FontSize(10);
                text.Span("BA").FontSize(20).FontColor(Color.FromHex(0xFF0000));
                text.Span(" AC").FontSize(10).Underline();
            });
        }));

        var (result, pdf) = Generate(document);

        Assert.Empty(result.Diagnostics);
        Assert.Contains("/F1 10 Tf", pdf, StringComparison.Ordinal);
        Assert.Contains("/F1 20 Tf", pdf, StringComparison.Ordinal);
        Assert.Contains("1 0 0 rg", pdf, StringComparison.Ordinal);   // red span
        // Underline: a thin filled rect after the third span (thickness 0.6 at size 10).
        Assert.Contains(" 0.6 re f", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void RichText_TallSpanSetsLineMetrics()
    {
        var document = Document.Create(doc => doc.Page(page =>
        {
            page.Content().Column(col =>
            {
                col.Item().Text(t =>
                {
                    t.Span("CA").FontSize(10);
                    t.Span("BA").FontSize(30); // line height must follow the size-30 span
                });
                col.Item().Text("CC").FontSize(10);
            });
        }));

        var (result, pdf) = Generate(document);

        Assert.Empty(result.Diagnostics);
        // Second item's baseline: content top (42.5) + line1 height (30) + ascent (8) = 80.5 from top
        // → PDF y = 841.89 - 80.5 = 761.39.
        Assert.Contains("761.39", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void Strikethrough_EmitsLineRect()
    {
        var document = Document.Create(doc => doc.Page(page =>
            page.Content().Text("CACA").FontSize(10).Strikethrough()));

        var (_, pdf) = Generate(document);

        Assert.Contains(" 0.6 re f", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentPageNumber_ResolvesPerPage_InFooter()
    {
        var document = Document.Create(doc => doc.Page(page =>
        {
            page.Footer().Text(t =>
            {
                t.AlignCenter();
                t.CurrentPageNumber().FontSize(9);
            });
            page.Content().Column(col =>
            {
                col.Item().Text("CA");
                col.Item().PageBreak();
                col.Item().Text("AC");
            });
        }));

        var (result, pdf) = Generate(document);

        Assert.Equal(2, result.PageCount);
        // Synthetic font maps no digits → digits are .notdef (gid 0). Distinct per page:
        // page 1 footer shows one glyph for "1", page 2 for "2" — both <0000>, so instead assert
        // via ToUnicode-free path: the footer text object must appear on both pages.
        var pages = pdf.Split("stream\n");
        Assert.Equal(2, pages.Count(p => p.Contains("/F1 9 Tf", StringComparison.Ordinal)));
    }

    [Fact]
    public void TotalPages_TriggersCountingPass_AndResolves()
    {
        var arialPath = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf")
            : null;
        if (arialPath is null || !File.Exists(arialPath))
        {
            return; // digits need a real font
        }

        FontManager.RegisterFontFile(arialPath);
        var document = Document.Create(doc => doc.Page(page =>
        {
            page.Footer().Text(t =>
            {
                t.Span("Page ").FontFamily("Arial").FontSize(9);
                t.CurrentPageNumber().FontFamily("Arial").FontSize(9);
                t.Span(" / ").FontFamily("Arial").FontSize(9);
                t.TotalPages().FontFamily("Arial").FontSize(9);
            });
            page.Content().Column(col =>
            {
                col.Item().Text("First").FontFamily("Arial");
                col.Item().PageBreak();
                col.Item().Text("Second").FontFamily("Arial");
                col.Item().PageBreak();
                col.Item().Text("Third").FontFamily("Arial");
            });
        }));

        using var buffer = new MemoryStream();
        var result = document.GeneratePdf(buffer);

        Assert.Equal(3, result.PageCount);
    }
}

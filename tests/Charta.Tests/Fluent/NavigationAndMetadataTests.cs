using System.Text;
using Charta.Cos;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Fluent;

public class NavigationAndMetadataTests
{
    private static readonly PdfWriterOptions ClassicUncompressed = new()
    {
        XrefMode = XrefMode.Classic,
        CompressStreams = false,
    };

    static NavigationAndMetadataTests() => FontManager.RegisterFont(SyntheticFont.Build());

    private static string Generate(Document document)
    {
        using var buffer = new MemoryStream();
        document.Generate(buffer, ClassicUncompressed, OverflowBehavior.Clip);
        return Encoding.ASCII.GetString(buffer.ToArray());
    }

    [Fact]
    public void Hyperlink_EmitsUriAnnotation()
    {
        var document = Document.Create(doc => doc.Page(page =>
            page.Content().Hyperlink("https://example.com/a").Text("AB")));

        var text = Generate(document);

        Assert.Contains("/Subtype /Link", text, StringComparison.Ordinal);
        Assert.Contains("/S /URI /URI (https://example.com/a)", text, StringComparison.Ordinal);
        Assert.Contains("/Annots", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SectionLink_EmitsNamedDestination()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Column(col =>
        {
            col.Item().SectionLink("details").Text("AB");
            col.Item().PageBreak();
            col.Item().Section("details").Text("BA");
        })));

        var text = Generate(document);

        Assert.Contains("/Dest (details)", text, StringComparison.Ordinal);
        Assert.Contains("/Names [(details) [", text, StringComparison.Ordinal);
        Assert.Contains("/XYZ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Bookmark_EmitsOutlineTree()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Column(col =>
        {
            col.Item().Bookmark("First part").Text("AB");
            col.Item().PageBreak();
            col.Item().Bookmark("Second part").Text("BA");
        })));

        var text = Generate(document);

        Assert.Contains("/Type /Outlines", text, StringComparison.Ordinal);
        Assert.Contains("/Title (First part)", text, StringComparison.Ordinal);
        Assert.Contains("/Title (Second part)", text, StringComparison.Ordinal);
        Assert.Contains("/Count 2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Metadata_EmitsInfoDictionaryAndXmp()
    {
        var document = Document.Create(doc =>
        {
            doc.Metadata(m => m
                .Title("Test Döküman")
                .Author("Utku")
                .CreationDate(new DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.FromHours(3))));
            doc.Page(page => page.Content().Text("AB"));
        });

        var text = Generate(document);

        Assert.Contains("/Producer (Charta)", text, StringComparison.Ordinal);
        Assert.Contains("/Author (Utku)", text, StringComparison.Ordinal);
        Assert.Contains("(D:20260709120000+03'00')", text, StringComparison.Ordinal);
        Assert.Contains("/Type /Metadata", text, StringComparison.Ordinal);
        Assert.Contains("<dc:creator><rdf:Seq><rdf:li>Utku</rdf:li></rdf:Seq></dc:creator>", text, StringComparison.Ordinal);
        Assert.Contains("2026-07-09T12:00:00+03:00", text, StringComparison.Ordinal);
        // Non-ASCII title goes to the Info dict as UTF-16BE (hex string with BOM).
        Assert.Contains("/Title <FEFF", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoMetadata_OmitsInfoAndXmp()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Text("AB")));

        var text = Generate(document);

        Assert.DoesNotContain("/Producer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/Metadata", text, StringComparison.Ordinal);
    }
}

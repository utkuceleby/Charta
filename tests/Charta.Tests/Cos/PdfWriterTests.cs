using System.Text;
using Charta.Cos;
using Xunit;

namespace Charta.Tests.Cos;

public class PdfWriterTests
{
    private static readonly PdfWriterOptions ClassicUncompressed = new()
    {
        XrefMode = XrefMode.Classic,
        CompressStreams = false,
    };

    private static readonly PdfWriterOptions StreamUncompressed = new()
    {
        XrefMode = XrefMode.Stream,
        CompressStreams = false,
    };

    private static byte[] Generate(PdfWriterOptions options)
    {
        using var buffer = new MemoryStream();
        HelloPdf.Write(buffer, options);
        return buffer.ToArray();
    }

    [Fact]
    public void Output_StartsWithHeader_AndEndsWithEof()
    {
        var bytes = Generate(ClassicUncompressed);

        Assert.StartsWith("%PDF-1.7\n", Encoding.ASCII.GetString(bytes, 0, 9));
        Assert.EndsWith("%%EOF\n", Encoding.ASCII.GetString(bytes, bytes.Length - 6, 6));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Output_IsDeterministic(bool useClassicXref)
    {
        var options = new PdfWriterOptions
        {
            XrefMode = useClassicXref ? XrefMode.Classic : XrefMode.Stream,
            CompressStreams = false,
        };

        var first = Generate(options);
        var second = Generate(options);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ClassicXref_StartxrefPointsAtXrefKeyword()
    {
        var bytes = Generate(ClassicUncompressed);
        var offset = ReadStartxref(bytes);

        Assert.Equal("xref", Encoding.ASCII.GetString(bytes, (int)offset, 4));
    }

    [Fact]
    public void XrefStream_StartxrefPointsAtIndirectObject()
    {
        var bytes = Generate(StreamUncompressed);
        var offset = ReadStartxref(bytes);
        var text = Encoding.ASCII.GetString(bytes, (int)offset, 12);

        Assert.Matches(@"^\d+ 0 obj", text);
    }

    [Fact]
    public void ClassicXref_EntriesAreExactlyTwentyBytes()
    {
        var bytes = Generate(ClassicUncompressed);
        var text = Encoding.ASCII.GetString(bytes);
        // "\nxref\n" so the search cannot match the "xref" inside "startxref".
        var xrefStart = text.LastIndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        var lines = text[xrefStart..].Split('\n');

        // lines[0] = "xref", lines[1] = "0 6", then one 20-byte entry per object (19 chars + \n).
        for (var i = 2; i < 8; i++)
        {
            Assert.Equal(19, lines[i].Length);
        }
    }

    [Fact]
    public void Writer_Throws_WhenAllocatedObjectIsNeverWritten()
    {
        using var buffer = new MemoryStream();
        using var writer = new PdfWriter(buffer, ClassicUncompressed);
        writer.WriteHeader();
        var root = writer.Allocate();
        writer.Allocate(); // never written

        writer.WriteObject(root, new CosDictionary());

        Assert.Throws<InvalidOperationException>(() => writer.WriteTrailer(root));
    }

    [Fact]
    public void Writer_Throws_OnDoubleWrite()
    {
        using var buffer = new MemoryStream();
        using var writer = new PdfWriter(buffer, ClassicUncompressed);
        writer.WriteHeader();
        var root = writer.Allocate();
        writer.WriteObject(root, new CosDictionary());

        Assert.Throws<InvalidOperationException>(() => writer.WriteObject(root, new CosDictionary()));
    }

    [Fact]
    public void CompressedContent_ProducesSmallerValidFile()
    {
        var uncompressed = Generate(ClassicUncompressed);
        var compressed = Generate(new PdfWriterOptions { XrefMode = XrefMode.Classic, CompressStreams = true });

        Assert.Contains("FlateDecode", Encoding.ASCII.GetString(compressed));
        Assert.NotEqual(uncompressed, compressed);
    }

    private static long ReadStartxref(byte[] bytes)
    {
        var text = Encoding.ASCII.GetString(bytes);
        var marker = text.LastIndexOf("startxref\n", StringComparison.Ordinal);
        Assert.True(marker >= 0, "startxref keyword not found");
        var rest = text[(marker + "startxref\n".Length)..];
        var line = rest[..rest.IndexOf('\n', StringComparison.Ordinal)];
        return long.Parse(line, System.Globalization.CultureInfo.InvariantCulture);
    }
}

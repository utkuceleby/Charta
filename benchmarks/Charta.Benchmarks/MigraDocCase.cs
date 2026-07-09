using BenchmarkDotNet.Attributes;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace Charta.Benchmarks;

public partial class PdfGenerationBenchmarks
{
    /// <summary>
    /// The Core build of PDFsharp/MigraDoc has no built-in font resolution — every consumer must
    /// implement IFontResolver by hand. This minimal Windows resolver is part of MigraDoc's real
    /// setup cost (Charta and QuestPDF need no equivalent).
    /// </summary>
    private sealed class WindowsFontResolver : IFontResolver
    {
        private static readonly string FontDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

        public byte[]? GetFont(string faceName) =>
            File.ReadAllBytes(Path.Combine(FontDirectory, faceName));

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
        {
            var file = (familyName.ToUpperInvariant(), bold, italic) switch
            {
                ("COURIER NEW", false, false) => "cour.ttf",
                ("COURIER NEW", true, false) => "courbd.ttf",
                ("COURIER NEW", false, true) => "couri.ttf",
                ("COURIER NEW", true, true) => "courbi.ttf",
                (_, true, false) => "arialbd.ttf",
                (_, false, true) => "ariali.ttf",
                (_, true, true) => "arialbi.ttf",
                _ => "arial.ttf",
            };
            return new FontResolverInfo(file);
        }
    }

    internal static void SetupMigraDocFonts() => GlobalFontSettings.FontResolver ??= new WindowsFontResolver();

    [Benchmark(Description = "MigraDoc")]
    public long GenerateWithMigraDoc()
    {
        var document = new MigraDoc.DocumentObjectModel.Document();
        var section = document.AddSection();
        var header = section.AddParagraph("Benchmark Report");
        header.Format.Font.Size = 18;
        header.Format.Font.Bold = true;
        for (var i = 0; i < Paragraphs; i++)
        {
            var paragraph = section.AddParagraph($"{i + 1}. {Paragraph}");
            paragraph.Format.Font.Size = 10;
            paragraph.Format.SpaceAfter = 8;
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var counter = new CountingStream();
        renderer.PdfDocument.Save(counter, closeStream: false);
        return counter.BytesWritten;
    }
}

using System.Security.Cryptography;
using Charta.Cos;
using Charta.Fonts;
using Charta.Imaging;
using Charta.Smoke;

// AOT/JIT parity smoke tool: writes the scaffolding documents and prints their SHA-256 hashes.
// CI publishes this with NativeAOT, runs both variants, and compares the hashes byte for byte.
// Also serves as the golden-file generator: pass a target directory to (re)write the golden PDFs.

var targetDirectory = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
Directory.CreateDirectory(targetDirectory);

var classicUncompressed = new PdfWriterOptions { XrefMode = XrefMode.Classic, CompressStreams = false };
var streamUncompressed = new PdfWriterOptions { XrefMode = XrefMode.Stream, CompressStreams = false };
var streamCompressed = new PdfWriterOptions { XrefMode = XrefMode.Stream, CompressStreams = true };

Emit("hello-classic.pdf", stream => HelloPdf.Write(stream, classicUncompressed));
Emit("hello-xrefstream.pdf", stream => HelloPdf.Write(stream, streamUncompressed));
Emit("hello-compressed.pdf", stream => HelloPdf.Write(stream, streamCompressed), golden: false);

var font = SyntheticFont.Build();
Emit("font-sample.pdf", stream => FontSampleDocument.Write(stream, font, "CAB", classicUncompressed));
Emit("font-sample-compressed.pdf", stream => FontSampleDocument.Write(stream, font, "CAB", streamCompressed), golden: false);

// 2x2 RGBA PNG (red, semi-green / blue, translucent white) — exercises decode, SMask, and placement.
byte[] rgbaPixels =
[
    255, 0, 0, 255, 0, 255, 0, 128,
    0, 0, 255, 255, 255, 255, 255, 64,
];
var png = PngFixtures.Build(2, 2, 8, colorType: 6, rgbaPixels, filterType: 4);
Emit("image-sample.pdf", stream => ImageSampleDocument.Write(stream, png, classicUncompressed));

Emit("layout-sample.pdf", stream => LayoutSample.Build().Generate(stream, classicUncompressed));
Emit("layout-sample-compressed.pdf", stream => LayoutSample.Build().Generate(stream, streamCompressed), golden: false);

Charta.FontManager.RegisterFont(font);
Emit("fluent-sample.pdf", stream => FluentSample.Build().Generate(stream, classicUncompressed, Charta.OverflowBehavior.Clip));
Emit("fluent-sample-compressed.pdf", stream => FluentSample.Build().GeneratePdf(stream), golden: false);

if (MultiScriptSample.Build() is { } multiScript)
{
    Emit("multiscript-sample.pdf", stream => multiScript.GeneratePdf(stream), golden: false);
}

// Optional second argument: a real font file to exercise the pipeline with (system-dependent, never golden).
if (args.Length > 1)
{
    var realFont = File.ReadAllBytes(args[1]);
    Emit(
        "real-font-sample.pdf",
        stream => FontSampleDocument.Write(stream, realFont, "Merhaba Charta 0123456789", streamCompressed),
        golden: false);
}

void Emit(string fileName, Action<Stream> write, bool golden = true)
{
    // Non-golden variants still exercise the writer under AOT and feed the qpdf CI check.
    var path = Path.Combine(targetDirectory, fileName);
    using (var file = File.Create(path))
    {
        write(file);
    }

    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    Console.WriteLine($"{fileName} {hash}{(golden ? string.Empty : " (not golden)")}");
}

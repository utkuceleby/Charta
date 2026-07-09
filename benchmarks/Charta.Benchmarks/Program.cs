using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace Charta.Benchmarks;

internal static class Program
{
    private static void Main(string[] args) => BenchmarkRunner.Run<PdfGenerationBenchmarks>(args: args);
}

/// <summary>
/// Head-to-head generation benchmarks: Charta vs QuestPDF vs MigraDoc/PDFsharp.
/// Each case writes the same logical document (a header plus repeating paragraphs) to a counting
/// null sink. Font setup differs per library (Charta/MigraDoc: system Arial; QuestPDF: bundled
/// Lato), so treat results as ballpark, not a font-rendering shootout.
/// </summary>
[MemoryDiagnoser]
public partial class PdfGenerationBenchmarks
{
    internal const string Paragraph =
        "Charta is a permanently-MIT PDF generation library for .NET with automatic line breaking, " +
        "kerning, and pagination. This paragraph repeats to fill pages with realistic flowing text.";

    /// <summary>10 ≈ a two-page letter; 200 ≈ a ~15-page report.</summary>
    [Params(10, 200)]
    public int Paragraphs { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        SetupMigraDocFonts();
        _ = GenerateWithCharta(); // warm the font-discovery cache outside measurement
    }

    /// <summary>Null sink that records how many bytes were produced.</summary>
    internal sealed class CountingStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => BytesWritten;

        public long BytesWritten { get; private set; }

        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => BytesWritten += count;

        public override void Write(ReadOnlySpan<byte> buffer) => BytesWritten += buffer.Length;

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

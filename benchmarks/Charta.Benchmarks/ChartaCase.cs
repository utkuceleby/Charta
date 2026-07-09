using BenchmarkDotNet.Attributes;

namespace Charta.Benchmarks;

public partial class PdfGenerationBenchmarks
{
    [Benchmark(Baseline = true, Description = "Charta")]
    public long GenerateWithCharta()
    {
        using var counter = new CountingStream();
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(50);
            page.Header().Text("Benchmark Report").FontSize(18).FontFamily("Arial").Bold();
            page.Content().Column(col =>
            {
                col.Spacing(8);
                for (var i = 0; i < Paragraphs; i++)
                {
                    col.Item().Text($"{i + 1}. {Paragraph}").FontFamily("Arial").FontSize(10);
                }
            });
        })).GeneratePdf(counter);
        return counter.BytesWritten;
    }
}

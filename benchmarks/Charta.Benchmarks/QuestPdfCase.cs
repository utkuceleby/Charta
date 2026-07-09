using BenchmarkDotNet.Attributes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace Charta.Benchmarks;

public partial class PdfGenerationBenchmarks
{
    [Benchmark(Description = "QuestPDF")]
    public long GenerateWithQuestPdf()
    {
        using var counter = new CountingStream();
        QuestPDF.Fluent.Document.Create(doc => doc.Page(page =>
        {
            page.Size(QuestPDF.Helpers.PageSizes.A4);
            page.Margin(50);
            page.Header().Text("Benchmark Report").FontSize(18).Bold();
            page.Content().Column(col =>
            {
                col.Spacing(8);
                for (var i = 0; i < Paragraphs; i++)
                {
                    col.Item().Text($"{i + 1}. {Paragraph}").FontSize(10);
                }
            });
        })).GeneratePdf(counter);
        return counter.BytesWritten;
    }
}

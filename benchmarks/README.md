# Benchmarks

Head-to-head PDF generation: Charta vs QuestPDF vs MigraDoc/PDFsharp. Same logical document
(header + N flowing paragraphs) written to a null sink.

Run:

```
dotnet run --project benchmarks/Charta.Benchmarks -c Release -- --filter *
```

## Snapshot (2026-07-09, short job)

Windows 10, .NET 10.0.8, x64. Charta/MigraDoc use system Arial; QuestPDF uses its bundled font.

| Library  | 10 paragraphs (~2 pages) | 200 paragraphs (~15 pages) | Allocated (200p) |
|----------|--------------------------|----------------------------|------------------|
| Charta   | **2.46 ms**              | **16.2 ms**                | 11.0 MB          |
| MigraDoc | 2.67 ms                  | 17.9 ms                    | 27.8 MB          |
| QuestPDF | 2.97 ms                  | 23.3 ms                    | 0.7 MB           |

Notes:

- QuestPDF's managed allocations look tiny because its layout and rendering run in a native Skia
  library — that memory is real but invisible to the GC diagnoser. Charta and MigraDoc are fully
  managed, so the numbers are comparable between those two.
- MigraDoc's Core build required implementing a custom `IFontResolver` before it would render at
  all (see `MigraDocCase.cs`); Charta and QuestPDF needed no font setup.
- Short-job numbers wobble a few percent between runs; treat ordering and magnitude as the signal.

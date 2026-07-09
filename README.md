# Charta

[![NuGet](https://img.shields.io/nuget/v/Charta.svg?logo=nuget)](https://www.nuget.org/packages/Charta)
[![Downloads](https://img.shields.io/nuget/dt/Charta.svg?logo=nuget)](https://www.nuget.org/packages/Charta)
[![CI](https://github.com/utkuceleby/Charta/actions/workflows/ci.yml/badge.svg)](https://github.com/utkuceleby/Charta/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A permanently-MIT PDF generation library for .NET — fluent layout API, zero native dependencies, streaming output, NativeAOT-ready.

**Why Charta?** Every free .NET PDF option forces a trade-off: the modern fluent engine is revenue-gated, the permissive veteran has no layout engine or text shaping, the feature king is AGPL. Charta is MIT forever — no revenue thresholds, no dual-license switch — with a 166KB package that depends on nothing but the BCL.

## Quick start

```csharp
using Charta;

var result = Document.Create(doc =>
{
    doc.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(2, Unit.Centimeter);
        page.Header().Text("Invoice #1042").FontSize(20).Bold();
        page.Content().Column(col =>
        {
            col.Spacing(10);
            col.Item().Text("Thanks for your purchase! Line breaking, kerning, and pagination are automatic.");
            col.Item().LineHorizontal(1);
            col.Item().Row(row =>
            {
                row.RelativeItem().Text("Total");
                row.ConstantItem(120).AlignRight().Text("€ 1.250,00").Bold();
            });
        });
        page.Footer().AlignCenter().Text("Page footer").FontSize(9);
    });
}).GeneratePdf("invoice.pdf");

// result.Diagnostics tells you if anything didn't fit — Charta clips and reports, it never throws.
```

Fonts resolve against explicitly registered files first (`FontManager.RegisterFont(...)` — the reproducible path for servers and containers), then against the operating system's installed fonts. Registered TrueType fonts are always subset and embedded.

### Script support today

| Scripts | Status |
|---|---|
| Latin (incl. Turkish, Polish, Vietnamese), Cyrillic, Greek | ✅ Full: correct rendering, kerning, and text extraction |
| CJK | ✅ Rendering, UAX#14 line breaking, and extraction (no vertical text yet) |
| Hebrew and mixed-direction text | ✅ Built-in UAX#9 bidi (100% conformant): correct reading order, mirrored brackets, correct extraction |
| Arabic, Indic, and other joining scripts | ✅ with the [`Charta.Shaping.HarfBuzz`](https://www.nuget.org/packages/Charta.Shaping.HarfBuzz) add-on — cursive joining, contextual forms, and marks. Without it, reading order is correct but letters render unjoined and a `LayoutDiagnostic` says so. |

```csharp
// Enable full shaping for Arabic/Indic (optional add-on, one line at startup):
Charta.Shaping.HarfBuzz.ChartaHarfBuzz.Register();
```

## Why another PDF library?

Every free .NET PDF option forces a trade-off: a modern fluent layout engine is revenue-gated, the permissively-licensed veteran has no modern layout engine or text shaping, and the most feature-complete library is AGPL. Charta aims to close that gap: a document generation engine that is MIT forever, has no revenue thresholds, no native binary payload in the core package, and treats digital signatures, complex-script text, and PDF/A + PDF/UA compliance as first-class free features.

## Cookbook

### Tables

```csharp
page.Content().Table(table =>
{
    table.ColumnsDefinition(cols =>
    {
        cols.RelativeColumn(3);
        cols.ConstantColumn(80);
        cols.RelativeColumn();
    });
    table.Header(header =>            // repeats at the top of every page
    {
        header.Cell().Background(Color.FromHex(0x1E5AA8)).Padding(6)
              .Text("Product").FontColor(Color.White).Bold();
        // ...
    });
    foreach (var row in rows)
    {
        table.Cell().Padding(6).Text(row.Name);
        table.Cell().Padding(6).AlignRight().Text($"{row.Qty}");
        table.Cell().Padding(6).AlignRight().Text($"{row.Total:N2}");
    }
});
```

Cells are ordinary containers (`Padding`, `Background`, `Border`, … all work) and support
`ColumnSpan`/`RowSpan`. Rows joined by a rowspan paginate as one unbreakable band.

### Page numbers and rich text

```csharp
page.Footer().Text(t =>
{
    t.AlignCenter();
    t.Span("Page ").FontSize(9);
    t.CurrentPageNumber().FontSize(9).Bold();
    t.Span(" of ").FontSize(9);
    t.TotalPages().FontSize(9);       // triggers an automatic counting pass
});
```

### Vector graphics and SVG

Draw vectors directly, or drop in an SVG (both managed, no extra dependency):

```csharp
page.Content().Width(150).Svg(File.ReadAllText("logo.svg"));

page.Content().Canvas(300, 120, canvas =>
{
    canvas.Rectangle(10, 10, 80, 100).Fill(Color.FromHex(0x1E5AA8));
    canvas.Circle(200, 60, 40).FillAndStroke(Color.White, Color.Black, 2);
});
```

### Digital signatures

The optional [`Charta.Signing`](https://www.nuget.org/packages/Charta.Signing) add-on signs a
document with an X.509 certificate — a PAdES B-B signature, built on the .NET crypto stack (no
BouncyCastle, no native dependencies):

```csharp
using Charta.Signing;

var signer = PdfSigners.FromCertificate(certificate);
Document.Create(doc => /* ... */)
    .GenerateSignedPdf("signed.pdf", signer, new SignatureInfo { Reason = "Approval" });
```

### Fonts on servers and in Docker

Register fonts explicitly — output becomes reproducible and independent of what the host has
installed. Registered fonts are always subset and embedded:

```csharp
FontManager.RegisterFontFile("fonts/Inter-Regular.ttf");
FontManager.RegisterFontFile("fonts/Inter-Bold.ttf");
// The first registered family is also the default when no FontFamily(...) is set.
```

Without registration Charta falls back to OS fonts (Windows font directory, fontconfig on Linux,
macOS font folders — no native calls).

### When something doesn't fit

Charta never throws for layout problems. Content that cannot fit even a full page is clipped and
reported in `result.Diagnostics` with what/where/why; treat a non-empty list as a warning in
development and log it in production. Prefer exceptions in CI? Opt in with
`new PdfSaveOptions { Overflow = OverflowBehavior.Throw }`.

## Design principles

- **MIT forever.** No dual-license switch, no revenue gates. The trademark is the only reserved right.
- **Zero dependencies in the core.** The `Charta` package references nothing but the BCL. Optional capabilities (HarfBuzz shaping, HTML rendering) live in opt-in add-on packages.
- **Never throw on overflow.** Content that does not fit is clipped, shrunk, or expanded according to policy — and reported through layout diagnostics. Exceptions are opt-in, for CI strictness.
- **Streaming by default.** Pages are serialized and flushed as they finish. Memory stays flat whether the document has 5 pages or 5,000.
- **NativeAOT and trimming clean.** No reflection, no runtime code generation. Verified by a publish-and-run gate in CI.

## Roadmap

| Milestone | Scope |
|---|---|
| M0 | Walking skeleton: streaming COS writer, valid "Hello PDF" |
| M1 | Real text: font parsing, subsetting, embedding; images |
| M2 | Layout engine: measure/arrange, pagination, overflow policy |
| M3 | Fluent API, first NuGet release |
| M4 | Tables with correct rowspan pagination |
| M5 | Digital signatures (PAdES B-B/B-T) |
| M6 | RTL and complex-script shaping (optional add-on) |
| M7 | PDF/A-2b/3b and PDF/UA |
| M8 | HTML/CSS subset rendering |

## License

[MIT](LICENSE)

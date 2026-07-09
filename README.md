# Charta

A permanently-MIT PDF generation library for .NET — fluent layout API, zero native dependencies, streaming output, NativeAOT-ready.

> **Status: pre-release.** The fluent API below works today; the surface may still shift before `0.1` ships on NuGet.

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

## Why another PDF library?

Every free .NET PDF option forces a trade-off: a modern fluent layout engine is revenue-gated, the permissively-licensed veteran has no modern layout engine or text shaping, and the most feature-complete library is AGPL. Charta aims to close that gap: a document generation engine that is MIT forever, has no revenue thresholds, no native binary payload in the core package, and treats digital signatures, complex-script text, and PDF/A + PDF/UA compliance as first-class free features.

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

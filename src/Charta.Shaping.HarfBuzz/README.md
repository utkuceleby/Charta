# Charta.Shaping.HarfBuzz

HarfBuzz text shaping for [Charta](https://www.nuget.org/packages/Charta) — correct cursive joining,
contextual forms, reordering, and mark positioning for Arabic, Syriac, Indic, and other complex
scripts.

Charta's core is dependency-free and shapes Latin, Cyrillic, Greek, CJK, and Hebrew correctly on its
own. This optional add-on plugs HarfBuzz in for the scripts that need full OpenType shaping. It is a
separate package because it carries a native HarfBuzz binary per platform — the core stays managed
and tiny.

## Usage

```csharp
using Charta.Shaping.HarfBuzz;

ChartaHarfBuzz.Register();   // once at startup

// From here on, Arabic joins correctly:
Document.Create(doc => doc.Page(page =>
    page.Content().Text("مرحبا بالعالم").FontFamily("Noto Sans Arabic")))
    .GeneratePdf("arabic.pdf");
```

Register a font that covers the script (`FontManager.RegisterFont(...)`) — the shaper needs the
font's GSUB/GPOS tables to do its work.

## License

[MIT](https://github.com/utkuceleby/Charta/blob/main/LICENSE)

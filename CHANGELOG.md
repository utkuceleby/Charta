# Changelog

All notable changes to Charta are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).
From 1.0.0 the public API is frozen: any change shows up as a reviewable diff in the API approval
file, and breaking changes require a major version.

## [1.0.0]

### Added

- Streaming PDF writer: classic and cross-reference-stream xref modes, Flate compression,
  deterministic output (content-derived file ID, no ambient clock).
- TrueType font pipeline: SFNT/TTC parsing, retain-GID subsetting with composite-glyph closure,
  Type0/Identity-H embedding with ToUnicode CMaps, GPOS pair kerning (glyph-pair and class-based).
- Cross-platform font discovery (Windows, macOS, fontconfig on Linux — no native calls) with
  `FontManager` registration taking precedence for reproducible server output.
- Managed PNG decoder (all filter types, 8/16-bit, indexed, transparency to SMask) and JPEG
  passthrough via DCTDecode.
- Layout engine: measure/draw element protocol with four-state pagination, never-throw overflow
  policy (clip + diagnostics; opt-in `Throw`), anti-hang circuit breakers, streaming page loop with
  flat memory, repeating header/footer bands.
- UAX#14 line breaking (Unicode 16.0), 100% conformant with the official test suite.
- UAX#9 bidirectional algorithm (Unicode 16.0), 100% conformant with both official test suites:
  Hebrew and mixed-direction text render and extract correctly, brackets mirror in RTL runs, and
  Arabic reading order is correct (letter joining ships later in the HarfBuzz add-on package).
- Tables: constant/relative columns, repeating headers, column and row spans with band pagination —
  spanned rows stay together, oversized bands clip with a diagnostic and the table continues.
- Rich text: styled spans in one paragraph, block alignment (left/center/right/justify),
  underline/strikethrough, per-page `CurrentPageNumber()` and `TotalPages()` (counting pre-pass).
- Fluent API: `Document.Create`, container extensions (padding, background, border, alignment,
  constraints, column, row, table, text, image, rules, page breaks, extend, layers), text styling,
  `IComponent`, hyperlinks, sections/internal links, outline bookmarks, document metadata
  (Info + XMP), `CancellationToken` support.
- Public API surface locked by an approval test.
- Benchmarks against QuestPDF and MigraDoc/PDFsharp (see `benchmarks/README.md`) and an examples
  gallery (`examples/`).

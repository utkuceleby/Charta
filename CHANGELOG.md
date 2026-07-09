# Changelog

All notable changes to Charta are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).
From 1.0.0 the public API is frozen: any change shows up as a reviewable diff in the API approval
file, and breaking changes require a major version.

## [1.4.0]

### Added

- Text polish: letter-spacing (tracking) on text and spans, superscript and subscript spans (shrunk
  and baseline-shifted), and `PdfSaveOptions.DebugLayout` — a development aid that marks clipped
  overflow regions with a red overlay in the output.

## [1.3.0]

### Added

- Vector graphics, all managed (no new dependency): a `Canvas(width, height, draw)` element with a
  path/paint drawing API (lines, cubic Béziers, rectangles, circles, ellipses; fill, stroke,
  fill-and-stroke), and `Svg(...)` / `SvgFile(...)` that render a practical SVG subset — paths,
  rect/circle/ellipse/line/polyline/polygon, groups with transforms, and fill/stroke presentation —
  scaled to the available width like a raster image.

## [1.2.0]

### Added

- **`Charta.Signing`** add-on package: PAdES digital signatures. `Document.GenerateSignedPdf(...)`
  with a signer from `PdfSigners.FromCertificate(cert)` produces an invisible PAdES B-B signature —
  a detached CMS SignedData over the whole document, SHA-256, with the ESS signing-certificate-v2
  attribute and the certificate chain. Built on the .NET cryptography stack only: no BouncyCastle,
  no native dependencies. Free digital signing is something no other permissively-licensed .NET PDF
  library offers.
- Core: `IPdfSigner` / `SignatureInfo` seam and `GenerateSignedPdf` overloads (stream and file). The
  core does only the PDF plumbing — the byte-range placeholder and patching — so it stays
  dependency-free; the cryptography lives in the add-on.

## [1.1.0]

### Added

- **`Charta.Shaping.HarfBuzz`** add-on package: full OpenType shaping for complex scripts. Call
  `ChartaHarfBuzz.Register()` and Arabic, Syriac, and Indic text joins, forms contextual glyphs,
  and positions marks correctly; ligatures still extract to their source text. The native HarfBuzz
  binary lives only in this opt-in package — the core stays managed and dependency-free.

### Changed

- Text shaping moved behind an internal `ITextShaper` seam (the add-on plugs into it). The built-in
  simple shaper is now cluster-aware for right-to-left text, and the RTL layout path shapes
  directional runs in logical order and orders them by rule L2. No public API change; left-to-right
  output is byte-identical.

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

# Changelog

All notable changes to Charta are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).
From 1.0.0 the public API is frozen: any change shows up as a reviewable diff in the API approval
file, and breaking changes require a major version.

## [1.13.0]

### Added

- **Password encryption (AES-256).** `new PdfSaveOptions { Encryption = new PdfEncryption { ... } }`
  encrypts the document with the PDF 2.0 standard security handler (V5, revision 6): Algorithm 2.B key
  derivation, a user and optional owner password, and `PdfPermissions` flags — all on the .NET crypto
  stack, no dependencies, core still zero-dependency. Every string and stream is encrypted with
  AES-256-CBC. Verified against pypdf (user/owner passwords authenticate, wrong passwords are
  rejected). Cannot be combined with a signature or a PDF/A / PDF/UA level, and encrypted output is
  not byte-reproducible (fresh random salts and keys each run).

## [1.12.0]

### Added

- **`Charta.Html`: percentage widths, `justify-content`, and clearer diagnostics.** Set
  `HtmlRenderOptions.ContentWidth` (usually the page width minus margins) and `width: 50%` resolves to
  points — against resolved parent widths when nested. Flex rows honor `justify-content` (`center`,
  `flex-end`, `space-between`, `space-around`, `space-evenly`) by distributing free space when items
  are fixed-width. `display: grid` and `@page` are now reported with guidance instead of being
  silently dropped, and resolve-time diagnostics (grid, cross-axis alignment, unresolved percentages)
  reliably reach `OnUnsupported`. Grid stays out of scope by design.

## [1.11.0]

### Added

- **`Charta.Html` grows a flexbox subset and more CSS.** `display: flex` maps to a row (or a column
  for `flex-direction: column`); items with a `width` become fixed columns and the rest share the
  space weighted by `flex-grow`, with `gap` honored. `white-space: pre` (and the `<pre>` element)
  now preserves runs of spaces and newlines instead of collapsing them, and `text-transform`
  (`uppercase`/`lowercase`/`capitalize`) is applied to text. Flex alignment/justification remain out
  of scope and are reported as usual.

## [1.10.1]

### Fixed

- **Parser robustness (found by fuzzing).** A malformed JPEG whose marker fill bytes run to the end
  of the file raised an `IndexOutOfRangeException`, and a PNG with an invalid zlib stream raised a raw
  `ZLibException`. Both now throw `ImageFormatException` like every other malformed-input case — the
  parsers' contract is upheld. Surfaced by the new SharpFuzz fuzzing of the SFNT/PNG/JPEG parsers.

## [1.10.0]

### Changed

- **Text shaping allocates far less.** The left-to-right shaping path no longer builds per-cluster
  lists (cluster grouping is only needed to reverse right-to-left runs), and font-run splitting slices
  by offset instead of rebuilding strings character by character. A 500-page text document now churns
  ~225 MB instead of ~850 MB — about 75% less — with byte-identical output. No API change.

## [1.9.0]

### Added

- **.notdef diagnostic for PDF/A and PDF/UA.** When generating with a conformance level, text drawn
  with a character the embedded font does not cover (which renders as .notdef — forbidden by both
  standards) now raises a `LayoutDiagnostic`, surfacing the most common compliance mistake before a
  validator does. At most one per page; outside PDF/A/UA nothing changes, and output bytes are
  unaffected either way.

## [1.8.0]

### Added

- **PAdES B-T signature timestamps** in `Charta.Signing`. Pass an `ITimestampAuthority` to
  `PdfSigners.FromCertificate(..., timestampAuthority: ...)` and the signer embeds an RFC 3161
  signature timestamp as an unsigned CMS attribute, raising the signature from PAdES B-B to B-T — the
  signing time is then asserted by a trusted third party rather than the signer. `TimestampAuthorities.Http(url)`
  reaches any RFC 3161 HTTP TSA; implement `ITimestampAuthority` yourself for offline or captured
  responses. Built on the .NET `Rfc3161TimestampRequest` stack — still no BouncyCastle, no native code.

## [1.7.0]

### Added

- **`Charta.Html`** — a new opt-in add-on that renders a subset of HTML/CSS to PDF. AngleSharp parses
  the markup; the CSS cascade (type/`.class`/`#id` selectors from `<style>` blocks and inline styles)
  and the layout are Charta's own — no browser, no native code. Covers block flow (headings,
  paragraphs, `div`/`section`, `blockquote`, `pre`, `hr`), inline styling (bold, italic, underline,
  strike-through, color, font size/family, super/subscript, links), lists, tables (with
  `colspan`/`rowspan`), and images (data URIs or file paths). Unsupported features are reported through
  `HtmlRenderOptions.OnUnsupported` and skipped — rendering never throws for unsupported markup.
  Entry point: `container.Html(html, options)`.

## [1.6.0]

### Added

- **PDF/UA-1** tagged, accessible PDF via `new PdfSaveOptions { Conformance = PdfConformance.PdfUA1, Language = "en-US" }`:
  a full structure tree (`StructTreeRoot`, `Document` root, `H1`–`H6`, `P`, `Figure`), marked
  content with a `ParentTree`, `/MarkInfo /Marked true`, `/Lang`, `/ViewerPreferences /DisplayDocTitle`,
  logical tab order, decorations (headers, footers, backgrounds, rules) emitted as artifacts, and
  pdfuaid XMP metadata. Verified compliant by the official veraPDF validator (106/106 rules) in CI.
  A document title is required. Tag text with `.Heading(1..6)` and give figures an alternate
  description via the new `altText` parameter on `.Image(...)`, `.Svg(...)`, and `.Canvas(...)`.
  Free accessibility conformance — another thing no other permissively-licensed .NET PDF library
  offers. (Provide a font covering all your text; like PDF/A, PDF/UA forbids showing the .notdef glyph.)

### Changed

- `ITextDescriptor` gained `Heading(int level)`; `Image`, `Svg`, and `Canvas` container extensions
  gained an optional `altText` argument; `PdfSaveOptions` gained a `Language` property.

## [1.5.0]

### Added

- **PDF/A-2b** archival conformance via `new PdfSaveOptions { Conformance = PdfConformance.PdfA2b }`:
  an embedded sRGB output intent (a compact ICC profile generated in code — no binary asset), pdfaid
  XMP metadata, print-flagged annotations, and always-embedded subset fonts. Verified compliant by
  the official veraPDF validator (144/144 rules) in CI. Free archival compliance — something no other
  permissively-licensed .NET PDF library offers. (Provide a font that covers all your text: PDF/A
  forbids showing the .notdef glyph.)

### Changed

- Link annotations now carry the Print flag.

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

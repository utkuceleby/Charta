using Charta.Fonts;

namespace Charta.FontDiscovery;

/// <summary>One face inside a font file: identifying names plus where to load the bytes from.</summary>
internal sealed class FontFace
{
    public required string FamilyName { get; init; }

    public required string PostScriptName { get; init; }

    public required bool IsBold { get; init; }

    public required bool IsItalic { get; init; }

    /// <summary>True for 'glyf' outlines (embeddable today); false for CFF-flavored faces.</summary>
    public required bool HasTrueTypeOutlines { get; init; }

    public required int CollectionIndex { get; init; }

    /// <summary>Set for files discovered on disk; null for faces registered from memory.</summary>
    public string? Path { get; init; }

    private readonly ReadOnlyMemory<byte> _data;
    private readonly object _gate = new();
    private ReadOnlyMemory<byte>? _loaded;
    private SfntFont? _parsed;

    public FontFace()
    {
    }

    public FontFace(ReadOnlyMemory<byte> data) => _data = data;

    public ReadOnlyMemory<byte> Load()
    {
        if (Path is null)
        {
            return _data;
        }

        lock (_gate)
        {
            return _loaded ??= File.ReadAllBytes(Path);
        }
    }

    /// <summary>
    /// The parsed font, cached: SfntFont is immutable, so one parse serves every document.
    /// Per-document state (glyph usage) lives in PdfFont, which wraps this.
    /// </summary>
    public SfntFont GetParsedFont()
    {
        lock (_gate)
        {
            return _parsed ??= SfntFont.Parse(Load(), CollectionIndex);
        }
    }
}

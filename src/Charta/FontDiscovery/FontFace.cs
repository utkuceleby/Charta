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

    public FontFace()
    {
    }

    public FontFace(ReadOnlyMemory<byte> data) => _data = data;

    public ReadOnlyMemory<byte> Load() => Path is null ? _data : File.ReadAllBytes(Path);
}

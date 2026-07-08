namespace Charta.Cos;

/// <summary>Controls how the cross-reference data is emitted.</summary>
internal enum XrefMode
{
    /// <summary>Cross-reference stream (ISO 32000-2 §7.5.8). Compact; requires PDF 1.5+ readers.</summary>
    Stream,

    /// <summary>Classic cross-reference table and trailer dictionary (§7.5.4). Maximum compatibility.</summary>
    Classic,
}

/// <summary>Writer configuration. Defaults produce compact production output; tests flip these for readable, diffable bytes.</summary>
internal sealed class PdfWriterOptions
{
    public static readonly PdfWriterOptions Default = new();

    public XrefMode XrefMode { get; init; } = XrefMode.Stream;

    /// <summary>When false, content streams are written uncompressed so golden tests produce human-readable diffs.</summary>
    public bool CompressStreams { get; init; } = true;
}

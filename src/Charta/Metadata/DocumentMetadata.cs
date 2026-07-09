namespace Charta.Metadata;

/// <summary>
/// Document properties written to both the classic Info dictionary and the XMP metadata stream.
/// CreationDate is caller-supplied only: no ambient clock, so identical input always produces
/// identical bytes.
/// </summary>
internal sealed class DocumentMetadata
{
    public string? Title { get; set; }

    public string? Author { get; set; }

    public string? Subject { get; set; }

    public string? Keywords { get; set; }

    public string? Creator { get; set; }

    public DateTimeOffset? CreationDate { get; set; }

    public bool HasAnyValue =>
        Title is not null || Author is not null || Subject is not null ||
        Keywords is not null || Creator is not null || CreationDate is not null;
}

namespace Charta;

/// <summary>
/// Produces the CMS/PKCS#7 signature container for a PDF signature. Implementations live in the
/// <c>Charta.Signing</c> add-on (certificate- and HSM-based signers); the core only does the PDF
/// plumbing — reserving the placeholder, computing the byte range, and embedding the result — so it
/// stays dependency-free.
/// </summary>
public interface IPdfSigner
{
    /// <summary>
    /// Bytes to reserve for the signature container. The <c>/Contents</c> placeholder is twice this
    /// in hex characters. Must comfortably exceed the actual container size (certificate chain and
    /// any timestamp); 16384 is a safe default.
    /// </summary>
    int ReserveBytes { get; }

    /// <summary>
    /// Signs the covered document bytes (everything except the signature placeholder) and returns
    /// the DER-encoded CMS SignedData container to embed.
    /// </summary>
    byte[] SignContent(ReadOnlySpan<byte> content);
}

/// <summary>Descriptive properties recorded in the PDF signature dictionary.</summary>
public sealed class SignatureInfo
{
    /// <summary>Why the document was signed (e.g. "I approve this document").</summary>
    public string? Reason { get; init; }

    /// <summary>Where the signing took place.</summary>
    public string? Location { get; init; }

    /// <summary>How to reach the signer.</summary>
    public string? ContactInfo { get; init; }

    /// <summary>The signer's name. When null, viewers derive it from the certificate.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// The claimed signing time. Charta never reads the system clock — set this explicitly, or leave
    /// it unset. A cryptographic timestamp (PAdES B-T) is stronger and is added by the signer.
    /// </summary>
    public DateTimeOffset? SigningTime { get; init; }
}

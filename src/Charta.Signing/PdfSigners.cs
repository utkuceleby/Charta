using System.Security.Cryptography.X509Certificates;
using Charta;

namespace Charta.Signing;

/// <summary>Creates <see cref="IPdfSigner"/> instances for <see cref="Document.GenerateSignedPdf(System.IO.Stream, IPdfSigner, SignatureInfo?, PdfSaveOptions?, System.Threading.CancellationToken)"/>.</summary>
public static class PdfSigners
{
    /// <summary>
    /// A signer backed by an X.509 certificate with a private key. Produces a PAdES B-B signature
    /// (detached CMS, SHA-256, signing-certificate-v2), or PAdES B-T when a
    /// <paramref name="timestampAuthority"/> is supplied — an RFC 3161 signature timestamp is then
    /// embedded as an unsigned attribute. Include intermediate/root certificates so validators can
    /// build the chain; set <paramref name="signingTime"/> for a claimed signing time (a timestamp is
    /// stronger, as it is asserted by a trusted third party rather than the signer).
    /// </summary>
    public static IPdfSigner FromCertificate(
        X509Certificate2 certificate,
        X509Certificate2Collection? additionalCertificates = null,
        DateTimeOffset? signingTime = null,
        ITimestampAuthority? timestampAuthority = null)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return new Pkcs7Signer(certificate, additionalCertificates, signingTime, timestampAuthority);
    }
}

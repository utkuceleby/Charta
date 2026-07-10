using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Charta.Signing;

/// <summary>
/// Signs with an X.509 certificate to PAdES baseline B-B: a detached CMS SignedData over the
/// document byte range, SHA-256 digest, with the ESS signing-certificate-v2 signed attribute and
/// the certificate chain embedded. When a timestamp authority is supplied, an RFC 3161 signature
/// timestamp is embedded as an unsigned attribute, raising the signature to PAdES B-T. Uses only the
/// .NET cryptography stack — no BouncyCastle.
/// </summary>
internal sealed class Pkcs7Signer : IPdfSigner
{
    // ESS signing-certificate-v2 (RFC 5035), the CAdES signature-time-stamp attribute, and SHA-256.
    private static readonly Oid SigningCertificateV2Oid = new("1.2.840.113549.1.9.16.2.47");
    private static readonly Oid SignatureTimeStampOid = new("1.2.840.113549.1.9.16.2.14");
    private static readonly Oid Sha256Oid = new("2.16.840.1.101.3.4.2.1");

    private readonly X509Certificate2 _certificate;
    private readonly X509Certificate2Collection _chain;
    private readonly DateTimeOffset? _signingTime;
    private readonly ITimestampAuthority? _timestampAuthority;

    public Pkcs7Signer(
        X509Certificate2 certificate,
        X509Certificate2Collection? additionalCertificates,
        DateTimeOffset? signingTime,
        ITimestampAuthority? timestampAuthority)
    {
        if (!certificate.HasPrivateKey)
        {
            throw new ArgumentException("The signing certificate must have an associated private key.", nameof(certificate));
        }

        _certificate = certificate;
        _chain = additionalCertificates ?? [];
        _signingTime = signingTime;
        _timestampAuthority = timestampAuthority;
    }

    // A signature timestamp adds a second CMS (TSA certificate chain included), so reserve more room.
    public int ReserveBytes => _timestampAuthority is null ? 16384 : 32768;

    public byte[] SignContent(ReadOnlySpan<byte> content)
    {
        var signedCms = new SignedCms(new ContentInfo(content.ToArray()), detached: true);

        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, _certificate)
        {
            DigestAlgorithm = Sha256Oid,
            IncludeOption = X509IncludeOption.None, // certificates added explicitly below
        };
        signer.Certificates.Add(_certificate);
        signer.Certificates.AddRange(_chain);

        // PAdES requires the CAdES signing-certificate-v2 attribute binding the signer certificate.
        signer.SignedAttributes.Add(BuildSigningCertificateV2(_certificate));
        if (_signingTime is { } time)
        {
            signer.SignedAttributes.Add(new Pkcs9SigningTime(time.UtcDateTime));
        }

        signedCms.ComputeSignature(signer);

        if (_timestampAuthority is { } tsa)
        {
            AddSignatureTimestamp(signedCms, tsa);
        }

        return signedCms.Encode();
    }

    /// <summary>
    /// Requests an RFC 3161 timestamp over the signer's signature value and embeds the returned token
    /// as the CAdES signature-time-stamp unsigned attribute (PAdES B-T).
    /// </summary>
    private static void AddSignatureTimestamp(SignedCms signedCms, ITimestampAuthority tsa)
    {
        var signerInfo = signedCms.SignerInfos[0];
        var request = Rfc3161TimestampRequest.CreateFromSignerInfo(
            signerInfo,
            HashAlgorithmName.SHA256,
            requestSignerCertificates: true);

        var responseBytes = tsa.RequestTimestamp(request.Encode());
        var token = request.ProcessResponse(responseBytes, out _);

        // The attribute value is the TimeStampToken (a CMS ContentInfo).
        var tokenBytes = token.AsSignedCms().Encode();
        signerInfo.AddUnsignedAttribute(new AsnEncodedData(SignatureTimeStampOid, tokenBytes));
    }

    /// <summary>ESSCertIDv2 with the SHA-256 hash of the certificate (default hash algorithm, no issuerSerial).</summary>
    private static AsnEncodedData BuildSigningCertificateV2(X509Certificate2 certificate)
    {
        var certHash = SHA256.HashData(certificate.RawData);

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())          // SigningCertificateV2
        using (writer.PushSequence())          // certs SEQUENCE OF ESSCertIDv2
        using (writer.PushSequence())          // ESSCertIDv2 (hashAlgorithm defaults to SHA-256)
        {
            writer.WriteOctetString(certHash); // certHash
        }

        return new AsnEncodedData(SigningCertificateV2Oid, writer.Encode());
    }
}

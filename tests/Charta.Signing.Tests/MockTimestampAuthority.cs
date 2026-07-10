using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Charta.Signing;

namespace Charta.Signing.Tests;

/// <summary>
/// A self-contained RFC 3161 timestamp authority for tests: it decodes the request, mints a
/// TSTInfo with a fixed time and serial, signs it with a timestamping certificate, and returns a
/// granted TimeStampResp. No network, fully deterministic.
/// </summary>
internal sealed class MockTimestampAuthority : ITimestampAuthority
{
    private const string IdCtTstInfo = "1.2.840.113549.1.9.16.1.4";
    private const string IdKpTimeStamping = "1.3.6.1.5.5.7.3.8";
    private const string TsaPolicy = "1.3.6.1.4.1.99999.1"; // an arbitrary private policy OID

    private readonly X509Certificate2 _tsaCertificate;
    private readonly DateTimeOffset _time;

    public MockTimestampAuthority(DateTimeOffset time)
    {
        _time = time;
        _tsaCertificate = CreateTsaCertificate(time);
    }

    public byte[] RequestTimestamp(ReadOnlyMemory<byte> request)
    {
        if (!Rfc3161TimestampRequest.TryDecode(request, out var req, out _))
        {
            throw new InvalidOperationException("Malformed timestamp request.");
        }

        var tstInfo = BuildTstInfo(req!, LiftMessageImprint(request));
        var content = new ContentInfo(new Oid(IdCtTstInfo), tstInfo);
        var cms = new SignedCms(content, detached: false);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, _tsaCertificate)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"), // SHA-256
            IncludeOption = X509IncludeOption.EndCertOnly,
        };
        signer.SignedAttributes.Add(BuildSigningCertificateV2(_tsaCertificate));
        cms.ComputeSignature(signer);

        return BuildResponse(cms.Encode());
    }

    private byte[] BuildTstInfo(Rfc3161TimestampRequest req, ReadOnlyMemory<byte> messageImprint)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(1); // version
            writer.WriteObjectIdentifier(TsaPolicy);
            writer.WriteEncodedValue(messageImprint.Span); // verbatim from the request — must match exactly
            writer.WriteInteger(new BigInteger(20260101)); // serialNumber
            writer.WriteGeneralizedTime(_time, omitFractionalSeconds: true);

            if (req.GetNonce() is { } nonce)
            {
                writer.WriteInteger(new BigInteger(nonce.Span, isUnsigned: true, isBigEndian: true));
            }
        }

        return writer.Encode();
    }

    /// <summary>Lifts the messageImprint SEQUENCE verbatim from the request DER so it matches byte-for-byte.</summary>
    private static ReadOnlyMemory<byte> LiftMessageImprint(ReadOnlyMemory<byte> request)
    {
        var outer = new AsnReader(request, AsnEncodingRules.DER);
        var req = outer.ReadSequence(); // TimeStampReq
        req.ReadInteger();              // version
        return req.ReadEncodedValue();  // messageImprint
    }

    private static byte[] BuildResponse(byte[] timeStampToken)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence()) // TimeStampResp
        {
            using (writer.PushSequence()) // PKIStatusInfo
            {
                writer.WriteInteger(0); // status: granted
            }

            writer.WriteEncodedValue(timeStampToken); // timeStampToken (ContentInfo)
        }

        return writer.Encode();
    }

    /// <summary>ESS signing-certificate-v2 (RFC 5035) binding the TSA certificate — required by RFC 3161 verifiers.</summary>
    private static AsnEncodedData BuildSigningCertificateV2(X509Certificate2 certificate)
    {
        var certHash = SHA256.HashData(certificate.RawData);
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        using (writer.PushSequence())
        using (writer.PushSequence())
        {
            writer.WriteOctetString(certHash);
        }

        return new AsnEncodedData(new Oid("1.2.840.113549.1.9.16.2.47"), writer.Encode());
    }

    private static X509Certificate2 CreateTsaCertificate(DateTimeOffset time)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Charta Test TSA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid(IdKpTimeStamping) }, critical: true));
        // The certificate must be valid at the timestamp instant, which callers set explicitly.
        return request.CreateSelfSigned(time.AddYears(-1), time.AddYears(1));
    }
}

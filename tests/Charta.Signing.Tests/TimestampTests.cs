using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Charta;
using Charta.Signing;
using Xunit;

namespace Charta.Signing.Tests;

public class TimestampTests
{
    private const string SignatureTimeStampOid = "1.2.840.113549.1.9.16.2.14";
    private static readonly DateTimeOffset TsaTime = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Charta Test Signer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    private static byte[] SignSample(X509Certificate2 cert, ITimestampAuthority? tsa)
    {
        var signer = PdfSigners.FromCertificate(cert, timestampAuthority: tsa);
        using var buffer = new MemoryStream();
        Document.Create(doc => doc.Page(page => page.Content().Text("Timestamped with Charta")))
            .GenerateSignedPdf(buffer, signer, new SignatureInfo { Reason = "Approval" });
        return buffer.ToArray();
    }

    private static SignedCms ExtractCms(byte[] pdf)
    {
        var latin = Encoding.Latin1.GetString(pdf);
        var contentsStart = latin.IndexOf("/Contents <", StringComparison.Ordinal) + "/Contents <".Length;
        var contentsEnd = latin.IndexOf('>', contentsStart);
        // The placeholder is zero-padded after the DER container; read exactly one TLV to drop it.
        var full = Convert.FromHexString(latin[contentsStart..contentsEnd]);
        var der = new System.Formats.Asn1.AsnReader(full, System.Formats.Asn1.AsnEncodingRules.BER)
            .ReadEncodedValue().ToArray();
        var cms = new SignedCms();
        cms.Decode(der);
        return cms;
    }

    [Fact]
    public void Timestamp_EmbedsVerifiableSignatureTimestamp()
    {
        using var cert = CreateSelfSignedCertificate();
        var pdf = SignSample(cert, new MockTimestampAuthority(TsaTime));

        var cms = ExtractCms(pdf);
        var signerInfo = cms.SignerInfos[0];
        var attribute = signerInfo.UnsignedAttributes
            .Cast<CryptographicAttributeObject>()
            .SingleOrDefault(a => a.Oid.Value == SignatureTimeStampOid);
        Assert.NotNull(attribute);

        var tokenBytes = attribute!.Values[0].RawData;
        Assert.True(Rfc3161TimestampToken.TryDecode(tokenBytes, out var token, out _));

        // The token must be a genuine timestamp over this signer's signature value.
        Assert.True(token!.VerifySignatureForSignerInfo(signerInfo, out _));
        Assert.Equal(TsaTime, token.TokenInfo.Timestamp);
    }

    [Fact]
    public void WithoutAuthority_NoTimestampAttribute()
    {
        using var cert = CreateSelfSignedCertificate();
        var pdf = SignSample(cert, tsa: null);

        var cms = ExtractCms(pdf);
        var hasTimestamp = cms.SignerInfos[0].UnsignedAttributes
            .Cast<CryptographicAttributeObject>()
            .Any(a => a.Oid.Value == SignatureTimeStampOid);
        Assert.False(hasTimestamp);
    }

    [Fact]
    public void Timestamp_ReservesEnoughRoomToEmbed()
    {
        // A timestamped signature must still fit the /Contents placeholder (larger reservation).
        using var cert = CreateSelfSignedCertificate();
        var pdf = SignSample(cert, new MockTimestampAuthority(TsaTime));
        Assert.StartsWith("%PDF", Encoding.Latin1.GetString(pdf, 0, 4));
    }
}

using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Charta;
using Charta.Signing;
using Xunit;

namespace Charta.Signing.Tests;

public class SigningTests
{
    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Charta Test Signer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    private static byte[] SignSample(X509Certificate2 cert, string text = "Signed with Charta")
    {
        var signer = PdfSigners.FromCertificate(cert, signingTime: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var buffer = new MemoryStream();
        Document.Create(doc => doc.Page(page => page.Content().Text(text)))
            .GenerateSignedPdf(buffer, signer, new SignatureInfo { Reason = "Approval", Location = "Istanbul" });
        return buffer.ToArray();
    }

    /// <summary>Parses ByteRange + Contents from our own output and returns (coveredContent, cmsDer).</summary>
    private static (byte[] Content, byte[] Cms) ExtractSignature(byte[] pdf)
    {
        var latin = Encoding.Latin1.GetString(pdf);
        var match = Regex.Match(latin, @"/ByteRange \[(\d+) (\d+) (\d+) (\d+)\]");
        Assert.True(match.Success, "ByteRange not found");
        var a = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var b = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        var c = int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        var d = int.Parse(match.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);

        var content = new byte[b + d];
        Array.Copy(pdf, a, content, 0, b);
        Array.Copy(pdf, c, content, b, d);

        var contentsStart = latin.IndexOf("/Contents <", StringComparison.Ordinal) + "/Contents <".Length;
        var contentsEnd = latin.IndexOf('>', contentsStart);
        var hex = latin[contentsStart..contentsEnd];
        var full = Convert.FromHexString(hex);
        var cms = full[..DerLength(full)]; // trim the zero padding after the DER container

        return (content, cms);
    }

    private static int DerLength(byte[] der)
    {
        var lengthByte = der[1];
        if ((lengthByte & 0x80) == 0)
        {
            return 2 + lengthByte;
        }

        var count = lengthByte & 0x7F;
        var length = 0;
        for (var i = 0; i < count; i++)
        {
            length = (length << 8) | der[2 + i];
        }

        return 2 + count + length;
    }

    [Fact]
    public void SignedPdf_HasValidStructure()
    {
        using var cert = CreateSelfSignedCertificate();
        var pdf = SignSample(cert);
        var text = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF", text);
        Assert.Contains("/Type /Sig", text);
        Assert.Contains("/SubFilter /ETSI.CAdES.detached", text);
        Assert.Contains("/AcroForm", text);
        Assert.Contains("/Reason (Approval)", text);
        Assert.EndsWith("%%EOF\n", text);
    }

    [Fact]
    public void Signature_VerifiesOverTheByteRange()
    {
        using var cert = CreateSelfSignedCertificate();
        var pdf = SignSample(cert);

        var (content, cms) = ExtractSignature(pdf);
        var signed = new SignedCms(new ContentInfo(content), detached: true);
        signed.Decode(cms);

        // Cryptographic verification only (the self-signed cert does not chain to a trusted root).
        signed.CheckSignature(verifySignatureOnly: true);

        var signerCert = signed.SignerInfos[0].Certificate;
        Assert.NotNull(signerCert);
        Assert.Equal(cert.Thumbprint, signerCert.Thumbprint);
    }

    [Fact]
    public void Signature_IncludesSigningCertificateV2Attribute()
    {
        using var cert = CreateSelfSignedCertificate();
        var pdf = SignSample(cert);

        var (content, cms) = ExtractSignature(pdf);
        var signed = new SignedCms(new ContentInfo(content), detached: true);
        signed.Decode(cms);

        var oids = signed.SignerInfos[0].SignedAttributes
            .Cast<System.Security.Cryptography.CryptographicAttributeObject>()
            .Select(a => a.Oid.Value)
            .ToList();

        Assert.Contains("1.2.840.113549.1.9.16.2.47", oids); // signing-certificate-v2
    }

    [Fact]
    public void TamperedContent_FailsVerification()
    {
        using var cert = CreateSelfSignedCertificate();
        var pdf = SignSample(cert, "Original text");

        // Flip a byte inside the first covered range (well before the signature).
        pdf[200] ^= 0xFF;

        var (content, cms) = ExtractSignature(pdf);
        var signed = new SignedCms(new ContentInfo(content), detached: true);
        signed.Decode(cms);

        Assert.ThrowsAny<CryptographicException>(() => signed.CheckSignature(verifySignatureOnly: true));
    }

    [Fact]
    public void Signer_WithoutPrivateKey_Throws()
    {
        using var cert = CreateSelfSignedCertificate();
        using var publicOnly = X509CertificateLoader.LoadCertificate(cert.Export(X509ContentType.Cert));

        Assert.Throws<ArgumentException>(() => PdfSigners.FromCertificate(publicOnly));
    }
}

# Charta.Signing

PAdES digital signatures for [Charta](https://www.nuget.org/packages/Charta) — sign generated PDFs
with an X.509 certificate. Uses only the .NET cryptography stack: no BouncyCastle, no native
dependencies.

## Usage

```csharp
using Charta;
using Charta.Signing;
using System.Security.Cryptography.X509Certificates;

var cert = X509CertificateLoader.LoadPkcs12FromFile("signer.pfx", "password");
var signer = PdfSigners.FromCertificate(cert);

Document.Create(doc => doc.Page(page => page.Content().Text("Signed with Charta")))
    .GenerateSignedPdf("signed.pdf", signer, new SignatureInfo
    {
        Reason = "I approve this document",
        Location = "Istanbul",
    });
```

The result carries an invisible signature over the whole document (PAdES baseline B-B): a detached
CMS SignedData with a SHA-256 digest and the ESS signing-certificate-v2 attribute. Include the
issuing chain via the `additionalCertificates` argument so validators can verify it.

## Trusted timestamps (PAdES B-T)

Pass an `ITimestampAuthority` to embed an RFC 3161 signature timestamp as an unsigned CMS attribute —
the signature becomes PAdES B-T, and the signing time is asserted by a trusted third party rather
than the signer:

```csharp
var tsa = TimestampAuthorities.Http(new Uri("https://freetsa.org/tsr"));
var signer = PdfSigners.FromCertificate(cert, timestampAuthority: tsa);
```

`TimestampAuthorities.Http` posts to any RFC 3161 HTTP TSA (optionally with basic auth). To route
through an offline TSA, a queue, or a captured response, implement `ITimestampAuthority` yourself —
it takes a DER `TimeStampReq` and returns the DER `TimeStampResp`.

## License

[MIT](https://github.com/utkuceleby/Charta/blob/main/LICENSE)

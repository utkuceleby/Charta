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

## License

[MIT](https://github.com/utkuceleby/Charta/blob/main/LICENSE)

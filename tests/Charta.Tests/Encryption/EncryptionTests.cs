using System.Text;
using Charta.Cos;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Encryption;

public class EncryptionTests
{
    private static readonly PdfWriterOptions ClassicUncompressed = new()
    {
        XrefMode = XrefMode.Classic,
        CompressStreams = false,
    };

    static EncryptionTests() => FontManager.RegisterFont(SyntheticFont.Build());

    private static string GenerateEncrypted(PdfEncryption encryption, string title = "TopSecretTitle")
    {
        var document = Document.Create(doc =>
        {
            doc.Metadata(m => m.Title(title));
            doc.Page(page => page.Content().Text("CAB"));
        });

        using var buffer = new MemoryStream();
        document.Generate(buffer, ClassicUncompressed, OverflowBehavior.Clip, encryption: encryption);
        return Encoding.Latin1.GetString(buffer.ToArray());
    }

    [Fact]
    public void WritesAes256StandardSecurityHandler()
    {
        var pdf = GenerateEncrypted(new PdfEncryption { UserPassword = "pw" });

        Assert.Contains("/Filter /Standard", pdf, StringComparison.Ordinal);
        Assert.Contains("/V 5", pdf, StringComparison.Ordinal);
        Assert.Contains("/R 6", pdf, StringComparison.Ordinal);
        Assert.Contains("/CFM /AESV3", pdf, StringComparison.Ordinal);
        Assert.Contains("/StmF /StdCF", pdf, StringComparison.Ordinal);
        Assert.Contains("/StrF /StdCF", pdf, StringComparison.Ordinal);
        Assert.Contains("/Encrypt ", pdf, StringComparison.Ordinal);
        foreach (var key in new[] { "/O ", "/U ", "/OE ", "/UE ", "/Perms " })
        {
            Assert.Contains(key, pdf, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EncryptsDocumentStringsSoTheyAreNotPlaintext()
    {
        // The document title is a string; encrypted, its cleartext must not appear in the output.
        var pdf = GenerateEncrypted(new PdfEncryption { UserPassword = "pw" }, title: "TopSecretTitle");
        Assert.DoesNotContain("TopSecretTitle", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void UnencryptedDocumentKeepsTitleInPlaintext()
    {
        // Control: without encryption the same title is visible, proving the previous test is meaningful.
        var document = Document.Create(doc =>
        {
            doc.Metadata(m => m.Title("TopSecretTitle"));
            doc.Page(page => page.Content().Text("CAB"));
        });
        using var buffer = new MemoryStream();
        document.Generate(buffer, ClassicUncompressed, OverflowBehavior.Clip);
        Assert.Contains("TopSecretTitle", Encoding.Latin1.GetString(buffer.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyUserPasswordStillEncrypts()
    {
        var pdf = GenerateEncrypted(new PdfEncryption { UserPassword = string.Empty });
        Assert.Contains("/Filter /Standard", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void EncryptionWithConformanceThrows()
    {
        var document = Document.Create(doc => doc.Page(page => page.Content().Text("CAB")));
        using var buffer = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => document.Generate(
            buffer, ClassicUncompressed, OverflowBehavior.Clip,
            conformance: PdfConformance.PdfA2b, encryption: new PdfEncryption { UserPassword = "pw" }));
    }

    [Fact]
    public void PermsReflectsRequestedPermissions()
    {
        // The P integer clears denied permission bits; a copy-only document must not carry the print bit.
        var pdf = GenerateEncrypted(new PdfEncryption { UserPassword = "pw", Permissions = PdfPermissions.Copy });
        var match = System.Text.RegularExpressions.Regex.Match(pdf, @"/P (-?\d+)");
        Assert.True(match.Success);
        var p = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(0, p & (int)PdfPermissions.Print);     // print denied
        Assert.NotEqual(0, p & (int)PdfPermissions.Copy);   // copy granted
    }
}

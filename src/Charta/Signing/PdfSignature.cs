using System.Globalization;
using System.Text;
using Charta.Cos;

namespace Charta.Signing;

/// <summary>A signing request threaded through generation.</summary>
internal sealed class SigningRequest(IPdfSigner signer, SignatureInfo info)
{
    public IPdfSigner Signer { get; } = signer;

    public SignatureInfo Info { get; } = info;
}

/// <summary>
/// Builds the PDF signature structures (an invisible signature field, its widget on the first page,
/// the AcroForm, and the signature value dictionary with ByteRange/Contents placeholders) and, after
/// the document is written to a seekable buffer, computes the byte range, invokes the signer, and
/// patches the placeholders in place. This is the standard two-pass approach — nothing is parsed.
/// </summary>
internal static class PdfSignature
{
    private const string ByteRangePlaceholder = "[0 0000000000 0000000000 0000000000]";

    /// <summary>Emits the signature value dictionary with placeholders. Returns its reference.</summary>
    public static CosReference WriteSignatureValue(PdfWriter writer, SigningRequest request)
    {
        var contentsHexLength = request.Signer.ReserveBytes * 2;
        var placeholder = "<" + new string('0', contentsHexLength) + ">";

        var sig = new CosDictionary
        {
            [CosNames.Type] = CosNames.Sig,
            [CosNames.Filter] = CosNames.AdobePpkLite,
            [CosNames.SubFilter] = CosNames.EtsiCadesDetached,
            [CosNames.ByteRange] = new CosRaw(ByteRangePlaceholder),
            [CosNames.Contents] = new CosRaw(placeholder),
        };

        var info = request.Info;
        if (info.Reason is { } reason)
        {
            sig[CosNames.Reason] = CosString.FromText(reason);
        }

        if (info.Location is { } location)
        {
            sig[CosNames.Location] = CosString.FromText(location);
        }

        if (info.ContactInfo is { } contact)
        {
            sig[CosNames.ContactInfo] = CosString.FromText(contact);
        }

        if (info.Name is { } name)
        {
            sig[CosNames.Name] = CosString.FromText(name);
        }

        if (info.SigningTime is { } time)
        {
            sig[CosNames.M] = CosString.FromAscii(FormatDate(time));
        }

        var sigRef = writer.Allocate();
        writer.WriteObject(sigRef, sig);
        return sigRef;
    }

    /// <summary>The signature field, merged with its widget annotation on the first page.</summary>
    public static CosDictionary BuildSignatureField(CosReference sigValueRef, CosReference pageRef)
    {
        return new CosDictionary
        {
            [CosNames.Type] = CosNames.Annot,
            [CosNames.Subtype] = CosNames.Widget,
            [CosNames.FT] = CosNames.Sig,
            [CosNames.T] = CosString.FromText("Signature1"),
            [CosNames.V] = sigValueRef,
            [CosNames.P] = pageRef,
            [CosNames.Rect] = CosArray.OfReals(0, 0, 0, 0), // invisible
            [CosNames.F] = new CosInteger(132),             // Print + Locked; no appearance needed
        };
    }

    /// <summary>The AcroForm dictionary referencing the signature field.</summary>
    public static CosDictionary BuildAcroForm(CosReference fieldRef) => new()
    {
        [CosNames.Fields] = new CosArray(fieldRef),
        [CosNames.SigFlags] = new CosInteger(3), // SignaturesExist | AppendOnly
    };

    /// <summary>
    /// Patches the ByteRange and Contents placeholders in a fully written document buffer: computes
    /// the byte range around the Contents hole, signs it, and writes the DER container as hex.
    /// </summary>
    public static void PatchSignature(byte[] pdf, IPdfSigner signer)
    {
        var contentsStart = IndexOf(pdf, "/Contents <"u8) + "/Contents ".Length; // points at '<'
        if (contentsStart < "/Contents ".Length)
        {
            throw new InvalidOperationException("Signature Contents placeholder not found.");
        }

        var contentsEnd = Array.IndexOf(pdf, (byte)'>', contentsStart); // inclusive of '>'
        if (contentsEnd < 0)
        {
            throw new InvalidOperationException("Signature Contents placeholder is unterminated.");
        }

        // ByteRange covers everything except the Contents value (including its < > delimiters).
        var range1Length = contentsStart;
        var range2Offset = contentsEnd + 1;
        var range2Length = pdf.Length - range2Offset;

        PatchByteRange(pdf, range1Length, range2Offset, range2Length);

        // The signed content is the two ranges concatenated.
        var content = new byte[range1Length + range2Length];
        Array.Copy(pdf, 0, content, 0, range1Length);
        Array.Copy(pdf, range2Offset, content, range1Length, range2Length);

        var container = signer.SignContent(content);
        var hex = Convert.ToHexString(container);
        var capacity = contentsEnd - contentsStart - 1; // characters between < and >
        if (hex.Length > capacity)
        {
            throw new InvalidOperationException(
                $"The signature container ({hex.Length / 2} bytes) exceeds the reserved space ({capacity / 2} bytes). Increase IPdfSigner.ReserveBytes.");
        }

        // Write hex then zero-fill the remainder of the placeholder.
        var hexBytes = Encoding.ASCII.GetBytes(hex);
        Array.Copy(hexBytes, 0, pdf, contentsStart + 1, hexBytes.Length);
        for (var i = contentsStart + 1 + hexBytes.Length; i < contentsEnd; i++)
        {
            pdf[i] = (byte)'0';
        }
    }

    private static void PatchByteRange(byte[] pdf, int length1, int offset2, int length2)
    {
        var marker = IndexOf(pdf, "/ByteRange "u8);
        if (marker < 0)
        {
            throw new InvalidOperationException("Signature ByteRange placeholder not found.");
        }

        var patched = string.Create(CultureInfo.InvariantCulture, $"[0 {length1:D10} {offset2:D10} {length2:D10}]");
        var bytes = Encoding.ASCII.GetBytes(patched);
        Array.Copy(bytes, 0, pdf, marker + "/ByteRange ".Length, bytes.Length);
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }

    private static string FormatDate(DateTimeOffset date)
    {
        var offset = date.Offset;
        var sign = offset >= TimeSpan.Zero ? '+' : '-';
        return string.Create(
            CultureInfo.InvariantCulture,
            $"D:{date:yyyyMMddHHmmss}{sign}{Math.Abs(offset.Hours):00}'{Math.Abs(offset.Minutes):00}'");
    }
}

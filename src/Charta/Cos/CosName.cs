using System.Collections.Concurrent;
using System.Text;

namespace Charta.Cos;

/// <summary>An interned PDF name object (ISO 32000-2 §7.3.5).</summary>
internal sealed class CosName : CosValue, IEquatable<CosName>
{
    private static readonly ConcurrentDictionary<string, CosName> Interned = new(StringComparer.Ordinal);

    public string Value { get; }

    private CosName(string value) => Value = value;

    public static CosName Get(string value) => Interned.GetOrAdd(value, static v => new CosName(v));

    public override void Write(PdfWriter writer)
    {
        var sb = new StringBuilder(Value.Length + 1);
        sb.Append('/');
        foreach (var b in Encoding.UTF8.GetBytes(Value))
        {
            // Regular characters may appear verbatim; everything else uses the #xx escape (§7.3.5).
            var isRegular = b is > 0x20 and < 0x7F && b is not ((byte)'#' or (byte)'/' or (byte)'(' or (byte)')'
                or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'%');
            if (isRegular)
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('#').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        writer.WriteAscii(sb.ToString());
    }

    public bool Equals(CosName? other) => ReferenceEquals(this, other);

    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => "/" + Value;
}

/// <summary>Pre-interned names used throughout the writer.</summary>
internal static class CosNames
{
    public static readonly CosName Type = CosName.Get("Type");
    public static readonly CosName Subtype = CosName.Get("Subtype");
    public static readonly CosName Catalog = CosName.Get("Catalog");
    public static readonly CosName Pages = CosName.Get("Pages");
    public static readonly CosName Page = CosName.Get("Page");
    public static readonly CosName Kids = CosName.Get("Kids");
    public static readonly CosName Count = CosName.Get("Count");
    public static readonly CosName Parent = CosName.Get("Parent");
    public static readonly CosName MediaBox = CosName.Get("MediaBox");
    public static readonly CosName Contents = CosName.Get("Contents");
    public static readonly CosName Resources = CosName.Get("Resources");
    public static readonly CosName Font = CosName.Get("Font");
    public static readonly CosName BaseFont = CosName.Get("BaseFont");
    public static readonly CosName Encoding = CosName.Get("Encoding");
    public static readonly CosName Length = CosName.Get("Length");
    public static readonly CosName Filter = CosName.Get("Filter");
    public static readonly CosName FlateDecode = CosName.Get("FlateDecode");
    public static readonly CosName Root = CosName.Get("Root");
    public static readonly CosName Size = CosName.Get("Size");
    public static readonly CosName Id = CosName.Get("ID");
    public static readonly CosName XRef = CosName.Get("XRef");
    public static readonly CosName W = CosName.Get("W");
    public static readonly CosName Index = CosName.Get("Index");
    public static readonly CosName Type0 = CosName.Get("Type0");
    public static readonly CosName CidFontType2 = CosName.Get("CIDFontType2");
    public static readonly CosName IdentityH = CosName.Get("Identity-H");
    public static readonly CosName Identity = CosName.Get("Identity");
    public static readonly CosName DescendantFonts = CosName.Get("DescendantFonts");
    public static readonly CosName ToUnicode = CosName.Get("ToUnicode");
    public static readonly CosName CidSystemInfo = CosName.Get("CIDSystemInfo");
    public static readonly CosName Registry = CosName.Get("Registry");
    public static readonly CosName Ordering = CosName.Get("Ordering");
    public static readonly CosName Supplement = CosName.Get("Supplement");
    public static readonly CosName FontDescriptor = CosName.Get("FontDescriptor");
    public static readonly CosName FontName = CosName.Get("FontName");
    public static readonly CosName Flags = CosName.Get("Flags");
    public static readonly CosName FontBBox = CosName.Get("FontBBox");
    public static readonly CosName ItalicAngle = CosName.Get("ItalicAngle");
    public static readonly CosName Ascent = CosName.Get("Ascent");
    public static readonly CosName Descent = CosName.Get("Descent");
    public static readonly CosName CapHeight = CosName.Get("CapHeight");
    public static readonly CosName StemV = CosName.Get("StemV");
    public static readonly CosName FontFile2 = CosName.Get("FontFile2");
    public static readonly CosName Length1 = CosName.Get("Length1");
    public static readonly CosName DW = CosName.Get("DW");
    public static readonly CosName CidToGidMap = CosName.Get("CIDToGIDMap");
}

using System.Text;

namespace Charta.Cos;

/// <summary>A PDF string object holding raw bytes (ISO 32000-2 §7.3.4).</summary>
internal sealed class CosString(byte[] bytes) : CosValue
{
    public byte[] Bytes { get; } = bytes;

    /// <summary>Creates a string from ASCII text.</summary>
    public static CosString FromAscii(string text) => new(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// Creates a text string (ISO 32000-2 §7.9.2.2): plain bytes when ASCII, UTF-16BE with BOM otherwise.
    /// </summary>
    public static CosString FromText(string text)
    {
        var ascii = true;
        foreach (var c in text)
        {
            if (c > 0x7E || (c < 0x20 && c is not ('\n' or '\r' or '\t')))
            {
                ascii = false;
                break;
            }
        }

        if (ascii)
        {
            return new CosString(Encoding.ASCII.GetBytes(text));
        }

        var payload = new byte[2 + Encoding.BigEndianUnicode.GetByteCount(text)];
        payload[0] = 0xFE;
        payload[1] = 0xFF;
        Encoding.BigEndianUnicode.GetBytes(text, payload.AsSpan(2));
        return new CosString(payload);
    }

    public override void Write(PdfWriter writer)
    {
        if (IsMostlyPrintable(Bytes))
        {
            WriteLiteral(writer);
        }
        else
        {
            WriteHex(writer);
        }
    }

    private void WriteLiteral(PdfWriter writer)
    {
        var sb = new StringBuilder(Bytes.Length + 2);
        sb.Append('(');
        foreach (var b in Bytes)
        {
            switch (b)
            {
                case (byte)'(' or (byte)')' or (byte)'\\':
                    sb.Append('\\').Append((char)b);
                    break;
                case >= 0x20 and < 0x7F:
                    sb.Append((char)b);
                    break;
                default:
                    sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                    break;
            }
        }

        sb.Append(')');
        writer.WriteAscii(sb.ToString());
    }

    private void WriteHex(PdfWriter writer)
    {
        var sb = new StringBuilder(Bytes.Length * 2 + 2);
        sb.Append('<');
        foreach (var b in Bytes)
        {
            sb.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        sb.Append('>');
        writer.WriteAscii(sb.ToString());
    }

    private static bool IsMostlyPrintable(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return true;
        }

        var printable = 0;
        foreach (var b in bytes)
        {
            if (b is >= 0x20 and < 0x7F)
            {
                printable++;
            }
        }

        return printable * 2 >= bytes.Length;
    }
}

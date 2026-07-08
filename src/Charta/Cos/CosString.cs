using System.Text;

namespace Charta.Cos;

/// <summary>A PDF string object holding raw bytes (ISO 32000-2 §7.3.4).</summary>
internal sealed class CosString(byte[] bytes) : CosValue
{
    public byte[] Bytes { get; } = bytes;

    /// <summary>Creates a string from ASCII text. Non-ASCII text handling arrives with the text layer.</summary>
    public static CosString FromAscii(string text) => new(Encoding.ASCII.GetBytes(text));

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

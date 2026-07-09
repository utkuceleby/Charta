using System.Text;

namespace Charta.Cos;

/// <summary>
/// Writes a fixed ASCII fragment verbatim — used for signature placeholders (the ByteRange array and
/// the Contents hex string) whose bytes are patched in place after the document is written.
/// </summary>
internal sealed class CosRaw(string ascii) : CosValue
{
    public override void Write(PdfWriter writer) => writer.WriteRaw(Encoding.ASCII.GetBytes(ascii));
}

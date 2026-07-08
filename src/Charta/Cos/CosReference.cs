namespace Charta.Cos;

/// <summary>An indirect object reference (ISO 32000-2 §7.3.10). Generation is always 0 in freshly generated files.</summary>
internal sealed class CosReference(int objectNumber) : CosValue
{
    public int ObjectNumber { get; } = objectNumber;

    public override void Write(PdfWriter writer) =>
        writer.WriteAscii($"{ObjectNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)} 0 R");
}

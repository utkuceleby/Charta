namespace Charta.Cos;

/// <summary>Base of the COS (Carousel Object System) value model defined in ISO 32000-2 §7.3.</summary>
internal abstract class CosValue
{
    public abstract void Write(PdfWriter writer);
}

internal sealed class CosNull : CosValue
{
    public static readonly CosNull Instance = new();

    private CosNull()
    {
    }

    public override void Write(PdfWriter writer) => writer.WriteAscii("null");
}

internal sealed class CosBoolean : CosValue
{
    public static readonly CosBoolean True = new(true);
    public static readonly CosBoolean False = new(false);

    private readonly bool _value;

    private CosBoolean(bool value) => _value = value;

    public override void Write(PdfWriter writer) => writer.WriteAscii(_value ? "true" : "false");
}

internal sealed class CosInteger(long value) : CosValue
{
    public long Value { get; } = value;

    public override void Write(PdfWriter writer) => writer.WriteAscii(Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

internal sealed class CosReal(double value) : CosValue
{
    public double Value { get; } = value;

    public override void Write(PdfWriter writer) => writer.WriteAscii(Format(Value));

    /// <summary>Formats with capped precision and no exponent so output stays deterministic across platforms.</summary>
    internal static string Format(double value) =>
        value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
}

namespace Charta.Cos;

/// <summary>A PDF array object (ISO 32000-2 §7.3.6).</summary>
internal sealed class CosArray : CosValue
{
    private readonly List<CosValue> _items = [];

    public CosArray()
    {
    }

    public CosArray(params CosValue[] items) => _items.AddRange(items);

    public int Count => _items.Count;

    public void Add(CosValue item) => _items.Add(item);

    public static CosArray OfReals(params double[] values)
    {
        var array = new CosArray();
        foreach (var value in values)
        {
            array.Add(new CosReal(value));
        }

        return array;
    }

    public static CosArray OfIntegers(params long[] values)
    {
        var array = new CosArray();
        foreach (var value in values)
        {
            array.Add(new CosInteger(value));
        }

        return array;
    }

    public override void Write(PdfWriter writer)
    {
        writer.WriteAscii("[");
        for (var i = 0; i < _items.Count; i++)
        {
            if (i > 0)
            {
                writer.WriteAscii(" ");
            }

            _items[i].Write(writer);
        }

        writer.WriteAscii("]");
    }
}

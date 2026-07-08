namespace Charta.Cos;

/// <summary>A PDF dictionary object (ISO 32000-2 §7.3.7). Preserves insertion order for deterministic output.</summary>
internal sealed class CosDictionary : CosValue
{
    private readonly List<KeyValuePair<CosName, CosValue>> _items = [];

    public CosValue this[CosName key]
    {
        set
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].Key.Equals(key))
                {
                    _items[i] = new KeyValuePair<CosName, CosValue>(key, value);
                    return;
                }
            }

            _items.Add(new KeyValuePair<CosName, CosValue>(key, value));
        }
    }

    public bool ContainsKey(CosName key)
    {
        foreach (var item in _items)
        {
            if (item.Key.Equals(key))
            {
                return true;
            }
        }

        return false;
    }

    public override void Write(PdfWriter writer)
    {
        writer.WriteAscii("<<");
        foreach (var (key, value) in _items)
        {
            writer.WriteAscii(" ");
            key.Write(writer);
            writer.WriteAscii(" ");
            value.Write(writer);
        }

        writer.WriteAscii(" >>");
    }
}

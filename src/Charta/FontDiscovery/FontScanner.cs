using System.Buffers.Binary;
using System.Text;
using Charta.Fonts;

namespace Charta.FontDiscovery;

/// <summary>
/// Lightweight face enumeration for discovery: reads only the table directory, 'name', and 'head' of
/// each face — cheap enough to scan a whole system font directory. Full parsing happens at use time.
/// </summary>
internal static class FontScanner
{
    private const uint TtcTag = 0x74746366;
    private const uint OttoTag = 0x4F54544F;
    private const uint TrueTypeVersion = 0x00010000;
    private const uint TrueTag = 0x74727565;

    /// <summary>Faces found in the data; empty (never throws) when the bytes are not a usable font.</summary>
    public static List<FontFace> Scan(ReadOnlyMemory<byte> data, string? path = null)
    {
        var faces = new List<FontFace>();
        try
        {
            var reader = new SfntReader(data.Span);
            var version = reader.ReadUInt32();
            if (version == TtcTag)
            {
                reader.Skip(4);
                var numFonts = reader.ReadUInt32();
                for (var i = 0; i < numFonts && i < 64; i++)
                {
                    var offset = reader.ReadUInt32();
                    if (offset <= int.MaxValue && ScanFace(data, (int)offset, i, path) is { } face)
                    {
                        faces.Add(face);
                    }
                }
            }
            else if (version is TrueTypeVersion or TrueTag or OttoTag)
            {
                if (ScanFace(data, 0, 0, path) is { } face)
                {
                    faces.Add(face);
                }
            }
        }
        catch (FontFormatException)
        {
            // Not a parsable font — discovery skips it.
        }

        return faces;
    }

    private static FontFace? ScanFace(ReadOnlyMemory<byte> data, int directoryOffset, int collectionIndex, string? path)
    {
        var reader = new SfntReader(data.Span) { Position = directoryOffset };
        var version = reader.ReadUInt32();
        if (version is not (TrueTypeVersion or TrueTag or OttoTag))
        {
            return null;
        }

        var numTables = reader.ReadUInt16();
        reader.Skip(6);

        ReadOnlyMemory<byte> name = default, head = default, os2 = default;
        for (var i = 0; i < numTables; i++)
        {
            var tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
            reader.Skip(4);
            var offset = reader.ReadUInt32();
            var length = reader.ReadUInt32();
            if (offset > int.MaxValue || offset + (long)length > data.Length)
            {
                return null;
            }

            switch (tag)
            {
                case "name":
                    name = data.Slice((int)offset, (int)length);
                    break;
                case "head":
                    head = data.Slice((int)offset, (int)length);
                    break;
                case "OS/2":
                    os2 = data.Slice((int)offset, (int)length);
                    break;
                default:
                    break;
            }
        }

        if (name.IsEmpty || head.Length < 46)
        {
            return null;
        }

        var macStyle = BinaryPrimitives.ReadUInt16BigEndian(head.Span[44..]);
        var isBold = (macStyle & 0x01) != 0;
        var family = ReadName(name, 16) ?? ReadName(name, 1);
        var postScript = ReadName(name, 6);
        if (family is null)
        {
            return null;
        }

        // OS/2 usWeightClass (offset 4) is the real weight axis and the only way to tell SemiBold
        // (600) from Bold (700); fall back to the coarse bold flag when the table is missing.
        var weight = os2.Length >= 6
            ? BinaryPrimitives.ReadUInt16BigEndian(os2.Span[4..])
            : (isBold ? 700 : 400);

        var face = new FontFace(path is null ? data : default)
        {
            FamilyName = family,
            PostScriptName = postScript ?? family,
            IsBold = isBold,
            IsItalic = (macStyle & 0x02) != 0,
            Weight = weight,
            HasTrueTypeOutlines = version != OttoTag,
            CollectionIndex = collectionIndex,
            Path = path,
        };
        return face;
    }

    /// <summary>Best-effort read of one name record, preferring Windows Unicode entries.</summary>
    private static string? ReadName(ReadOnlyMemory<byte> name, ushort nameId)
    {
        try
        {
            var reader = new SfntReader(name.Span);
            reader.Skip(2);
            var count = reader.ReadUInt16();
            var stringOffset = reader.ReadUInt16();

            string? fallback = null;
            for (var i = 0; i < count; i++)
            {
                var platformId = reader.ReadUInt16();
                var encodingId = reader.ReadUInt16();
                reader.Skip(2);
                var id = reader.ReadUInt16();
                var length = reader.ReadUInt16();
                var offset = reader.ReadUInt16();
                if (id != nameId || stringOffset + offset + length > name.Length)
                {
                    continue;
                }

                var bytes = name.Span.Slice(stringOffset + offset, length);
                switch (platformId, encodingId)
                {
                    case (3, 1) or (3, 10) or (0, _):
                        return Encoding.BigEndianUnicode.GetString(bytes);
                    case (1, 0):
                        fallback = Encoding.Latin1.GetString(bytes);
                        break;
                    default:
                        break;
                }
            }

            return fallback;
        }
        catch (FontFormatException)
        {
            return null;
        }
    }
}

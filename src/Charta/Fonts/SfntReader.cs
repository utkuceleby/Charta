using System.Buffers.Binary;

namespace Charta.Fonts;

/// <summary>Bounds-checked big-endian reader over SFNT data. Out-of-range reads throw <see cref="FontFormatException"/>.</summary>
internal ref struct SfntReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;

    public int Position { get; set; }

    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));

    public short ReadInt16() => BinaryPrimitives.ReadInt16BigEndian(Take(2));

    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4));

    public int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(Take(4));

    public ReadOnlySpan<byte> ReadBytes(int count) => Take(count);

    public void Skip(int count) => _ = Take(count);

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || Position > _data.Length - count)
        {
            throw new FontFormatException($"Unexpected end of font data reading {count} bytes at offset {Position}.");
        }

        var slice = _data.Slice(Position, count);
        Position += count;
        return slice;
    }
}

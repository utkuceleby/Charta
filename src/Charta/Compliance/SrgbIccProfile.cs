using System.Buffers.Binary;
using System.Text;

namespace Charta.Compliance;

/// <summary>
/// Builds a minimal, well-formed sRGB ICC v2 profile (matrix/TRC, D50 PCS) for the PDF/A output
/// intent. Small (~450 bytes) and generated in code so the core carries no binary asset; the values
/// are the standard Bradford-adapted sRGB primaries and a sRGB tone curve.
/// </summary>
internal static class SrgbIccProfile
{
    public static byte[] Build()
    {
        // Tag payloads.
        var desc = BuildTextDescription("sRGB");
        var cprt = BuildText("Charta sRGB");
        var wtpt = BuildXyz(0.96420, 1.00000, 0.82491);      // D50
        var rXYZ = BuildXyz(0.43607, 0.22249, 0.01392);       // sRGB primaries adapted to D50
        var gXYZ = BuildXyz(0.38515, 0.71687, 0.09708);
        var bXYZ = BuildXyz(0.14307, 0.06061, 0.71410);
        var trc = BuildGammaCurve(2.2);

        // Tags; rTRC/gTRC/bTRC share one curve payload.
        var tags = new List<(string Sig, byte[] Data, bool Shared)>
        {
            ("desc", desc, false),
            ("wtpt", wtpt, false),
            ("rXYZ", rXYZ, false),
            ("gXYZ", gXYZ, false),
            ("bXYZ", bXYZ, false),
            ("rTRC", trc, false),
            ("gTRC", trc, true),
            ("bTRC", trc, true),
            ("cprt", cprt, false),
        };

        const int headerSize = 128;
        var tableSize = 4 + tags.Count * 12;
        var dataStart = Align4(headerSize + tableSize);

        // Lay out payloads, sharing identical curve data.
        var offsets = new int[tags.Count];
        var sizes = new int[tags.Count];
        var payload = new List<byte>();
        var curveOffset = -1;
        for (var i = 0; i < tags.Count; i++)
        {
            if (tags[i].Shared && curveOffset >= 0)
            {
                offsets[i] = curveOffset;
                sizes[i] = tags[i].Data.Length;
                continue;
            }

            var offset = dataStart + payload.Count;
            offsets[i] = offset;
            sizes[i] = tags[i].Data.Length;
            payload.AddRange(tags[i].Data);
            while (payload.Count % 4 != 0)
            {
                payload.Add(0);
            }

            if (tags[i].Sig == "rTRC")
            {
                curveOffset = offset;
            }
        }

        var totalSize = dataStart + payload.Count;
        var profile = new byte[totalSize];

        WriteHeader(profile, totalSize);

        // Tag table.
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(headerSize), (uint)tags.Count);
        for (var i = 0; i < tags.Count; i++)
        {
            var entry = profile.AsSpan(headerSize + 4 + i * 12);
            Encoding.ASCII.GetBytes(tags[i].Sig, entry);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)offsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)sizes[i]);
        }

        payload.CopyTo(profile, dataStart);
        return profile;
    }

    private static void WriteHeader(byte[] p, int size)
    {
        BinaryPrimitives.WriteUInt32BigEndian(p, (uint)size);          // profile size
        BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(8), 0x02100000); // version 2.1
        Encoding.ASCII.GetBytes("mntr", p.AsSpan(12));                  // device class
        Encoding.ASCII.GetBytes("RGB ", p.AsSpan(16));                  // data colour space
        Encoding.ASCII.GetBytes("XYZ ", p.AsSpan(20));                  // PCS

        // Fixed date 2026-01-01 (no ambient clock).
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(24), 2026);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(28), 1);

        Encoding.ASCII.GetBytes("acsp", p.AsSpan(36));                  // required signature
        BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(64), 0);         // rendering intent: perceptual

        // PCS illuminant = D50 as s15Fixed16.
        BinaryPrimitives.WriteInt32BigEndian(p.AsSpan(68), Fixed(0.96420));
        BinaryPrimitives.WriteInt32BigEndian(p.AsSpan(72), Fixed(1.00000));
        BinaryPrimitives.WriteInt32BigEndian(p.AsSpan(76), Fixed(0.82491));
    }

    private static byte[] BuildXyz(double x, double y, double z)
    {
        var data = new byte[20];
        Encoding.ASCII.GetBytes("XYZ ", data);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), Fixed(x));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), Fixed(y));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), Fixed(z));
        return data;
    }

    private static byte[] BuildGammaCurve(double gamma)
    {
        var data = new byte[14];
        Encoding.ASCII.GetBytes("curv", data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 1);              // one entry = gamma
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12), (ushort)Math.Round(gamma * 256)); // u8Fixed8
        return data;
    }

    private static byte[] BuildText(string text)
    {
        var ascii = Encoding.ASCII.GetBytes(text);
        var data = new byte[8 + ascii.Length + 1];
        Encoding.ASCII.GetBytes("text", data);
        ascii.CopyTo(data, 8);
        return data; // trailing null already zero
    }

    private static byte[] BuildTextDescription(string text)
    {
        var ascii = Encoding.ASCII.GetBytes(text);
        var asciiCount = ascii.Length + 1; // includes null
        var data = new byte[8 + 4 + asciiCount + 8 + 3 + 67];
        Encoding.ASCII.GetBytes("desc", data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)asciiCount);
        ascii.CopyTo(data, 12);
        // Unicode (lang code + count 0) and ScriptCode (code 0, count 0, 67-byte buffer) stay zero.
        return data;
    }

    private static int Fixed(double value) => (int)Math.Round(value * 65536.0);

    private static int Align4(int value) => (value + 3) & ~3;
}

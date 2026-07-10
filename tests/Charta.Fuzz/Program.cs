using Charta.Fonts;
using Charta.Imaging;
using Charta.Smoke;
using SharpFuzz;

// Coverage-guided fuzz harness for Charta's binary parsers, driven by libfuzzer-dotnet + SharpFuzz.
//
//   Charta.Fuzz seed <dir>   writes the seed corpora (sfnt/png/jpeg subdirectories) and exits.
//   otherwise                runs the fuzzer; FUZZ_TARGET (sfnt|png|jpeg) selects the parser.
//
// The contract each parser must uphold on any input: throw its own format exception or succeed —
// never any other exception, and never hang or run out of memory (libFuzzer's rss/time limits catch
// those). Only the expected format exception is swallowed; anything else escapes and is a finding.

if (args.Length >= 2 && args[0] == "seed")
{
    WriteSeeds(args[1]);
    return;
}

var target = Environment.GetEnvironmentVariable("FUZZ_TARGET") ?? "sfnt";
switch (target)
{
    case "png":
        Fuzzer.LibFuzzer.Run(data => FuzzPng(data.ToArray()));
        break;
    case "jpeg":
        Fuzzer.LibFuzzer.Run(data => FuzzJpeg(data.ToArray()));
        break;
    default:
        Fuzzer.LibFuzzer.Run(data => FuzzSfnt(data.ToArray()));
        break;
}

static void FuzzSfnt(ReadOnlyMemory<byte> data)
{
    try
    {
        var font = SfntFont.Parse(data);
        // Exercise the tables the layout engine touches, so malformed offsets are hit too.
        for (ushort gid = 0; gid < font.NumGlyphs; gid++)
        {
            try
            {
                _ = font.GetGlyphData(gid);
            }
            catch (FontFormatException)
            {
            }
        }

        _ = font.MapCodepoint('A');
    }
    catch (FontFormatException)
    {
    }
}

static void FuzzPng(ReadOnlyMemory<byte> data)
{
    try
    {
        _ = PngDecoder.Decode(data);
    }
    catch (ImageFormatException)
    {
    }
}

static void FuzzJpeg(ReadOnlyMemory<byte> data)
{
    try
    {
        _ = JpegParser.Parse(data);
    }
    catch (ImageFormatException)
    {
    }
}

static void WriteSeeds(string root)
{
    // sfnt: the synthetic TrueType font (and a TTC-ish variant via a different first glyph).
    var sfnt = Path.Combine(root, "sfnt");
    Directory.CreateDirectory(sfnt);
    File.WriteAllBytes(Path.Combine(sfnt, "synthetic.ttf"), SyntheticFont.Build());
    File.WriteAllBytes(Path.Combine(sfnt, "synthetic-b.ttf"), SyntheticFont.Build('B'));

    // png: one seed per color type / filter the decoder supports.
    var png = Path.Combine(root, "png");
    Directory.CreateDirectory(png);
    byte[] rgba = [255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 255, 255, 255, 255, 64];
    File.WriteAllBytes(Path.Combine(png, "rgba.png"), PngFixtures.Build(2, 2, 8, colorType: 6, rgba, filterType: 4));
    byte[] gray = [0, 10, 20, 30, 40, 50];
    File.WriteAllBytes(Path.Combine(png, "gray.png"), PngFixtures.Build(2, 1, 8, colorType: 0, gray));

    // jpeg: a baseline frame header (the parser reads the SOF, not pixel data).
    var jpeg = Path.Combine(root, "jpeg");
    Directory.CreateDirectory(jpeg);
    File.WriteAllBytes(Path.Combine(jpeg, "header.jpg"), PngFixtures.BuildJpegHeader(16, 16));

    Console.WriteLine($"Seeds written under {Path.GetFullPath(root)}");
}

using Charta.Fonts;
using Charta.Imaging;
using SharpFuzz;

// Coverage-guided fuzz harness for Charta's binary parsers, driven by libfuzzer-dotnet + SharpFuzz.
// FUZZ_TARGET (sfnt|png|jpeg) selects the parser; seed corpora live under corpus/ in the repository.
//
// The contract each parser must uphold on any input: throw its own format exception or succeed —
// never any other exception, and never hang or run out of memory (libFuzzer's rss/time limits catch
// those). Only the expected format exception is swallowed; anything else escapes and is a finding.

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

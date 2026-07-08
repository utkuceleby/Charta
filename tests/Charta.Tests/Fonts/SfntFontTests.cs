using Charta.Fonts;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Fonts;

public class SfntFontTests
{
    private static SfntFont Parse() => SfntFont.Parse(SyntheticFont.Build());

    [Fact]
    public void Parse_ReadsHeaderTables()
    {
        var font = Parse();

        Assert.Equal(1000, font.UnitsPerEm);
        Assert.Equal(4, font.NumGlyphs);
        Assert.Equal(800, font.Ascender);
        Assert.Equal(-200, font.Descender);
        Assert.Equal("ChartaTest", font.PostScriptName);
        Assert.Equal(0, font.ItalicAngle);
    }

    [Theory]
    [InlineData('A', 1)]
    [InlineData('B', 2)]
    [InlineData('C', 3)]
    [InlineData('Z', 0)]
    [InlineData(' ', 0)]
    public void MapCodepoint_UsesCmapFormat4(char c, int expectedGid)
    {
        Assert.Equal(expectedGid, Parse().MapCodepoint(c));
    }

    [Theory]
    [InlineData(0, 500)]
    [InlineData(1, 600)]
    [InlineData(3, 600)]
    public void AdvanceWidth_ReadsHmtx(int gid, int expected)
    {
        Assert.Equal(expected, Parse().AdvanceWidth((ushort)gid));
    }

    [Fact]
    public void GetGlyphData_EmptyForNotdef_NonEmptyForOutlines()
    {
        var font = Parse();

        Assert.Equal(0, font.GetGlyphData(0).Length);
        Assert.True(font.GetGlyphData(1).Length > 0);
        Assert.Throws<FontFormatException>(() => font.GetGlyphData(4));
    }

    [Fact]
    public void Parse_RejectsCffFlavoredFonts()
    {
        var otto = new byte[] { 0x4F, 0x54, 0x54, 0x4F, 0, 0, 0, 0, 0, 0, 0, 0 };

        var ex = Assert.Throws<FontFormatException>(() => SfntFont.Parse(otto));
        Assert.Contains("CFF", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_GarbageInput_ThrowsOnlyFontFormatException()
    {
        // Deterministic pseudo-fuzz: the parser contract is FontFormatException or success, never
        // any other exception type. The real fuzz harness extends this; this is the cheap CI floor.
        var valid = SyntheticFont.Build();
        for (var seed = 0; seed < 500; seed++)
        {
            var random = new Random(seed);
            var data = new byte[random.Next(0, valid.Length * 2)];
            random.NextBytes(data);

            // Half the corpus: valid font with a few corrupted bytes (more interesting paths).
            if (seed % 2 == 0)
            {
                data = (byte[])valid.Clone();
                for (var i = 0; i < 8; i++)
                {
                    data[random.Next(data.Length)] = (byte)random.Next(256);
                }
            }

            try
            {
                var font = SfntFont.Parse(data);
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
    }
}

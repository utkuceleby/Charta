using Charta.Fonts;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Fonts;

public class GposKerningTests
{
    [Fact]
    public void TryCreate_ReturnsNullWithoutGpos()
    {
        // A subset drops GPOS, so it makes a convenient GPOS-free fixture.
        var font = SfntFont.Parse(SyntheticFont.Build());
        var subset = SfntFont.Parse(TrueTypeSubsetter.CreateSubset(font, GlyphClosure.Compute(font, [(ushort)1])));

        Assert.Null(GposKerning.TryCreate(subset));
    }

    [Fact]
    public void GetAdjustment_ReadsPairPosFormat1()
    {
        var font = SfntFont.Parse(SyntheticFont.Build());
        var kerning = GposKerning.TryCreate(font);

        Assert.NotNull(kerning);
        Assert.Equal(-40, kerning.GetAdjustment(1, 2)); // A followed by B
        Assert.Equal(0, kerning.GetAdjustment(2, 1));    // B followed by A: no pair
        Assert.Equal(0, kerning.GetAdjustment(1, 3));
        Assert.Equal(0, kerning.GetAdjustment(999, 2));  // out-of-range glyph
    }

    [Fact]
    public void Shape_AppliesKerningToWidthAndOperator()
    {
        var font = PdfFont.Parse(SyntheticFont.Build());

        var shaped = font.Shape("AB");

        Assert.Equal(1160, shaped.Width); // 600 + 600 - 40 kern
        Assert.Equal(-40, shaped.Glyphs[0].KernAfter);
        Assert.Equal("[<0001> 40 <0002>] TJ", shaped.ToTextOperator());
    }

    [Fact]
    public void Shape_UnkernedTextUsesPlainTj()
    {
        var font = PdfFont.Parse(SyntheticFont.Build());

        var shaped = font.Shape("BA"); // no kern pair in this direction

        Assert.Equal("<00020001> Tj", shaped.ToTextOperator());
    }

    [Fact]
    public void Arial_KernsAVPair()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf");
        if (!File.Exists(path))
        {
            return;
        }

        var font = SfntFont.Parse(File.ReadAllBytes(path));
        var kerning = GposKerning.TryCreate(font);
        if (kerning is null)
        {
            return; // some Arial builds ship kerning in the legacy 'kern' table only
        }

        // 'A' followed by 'V' is the canonical negative kern pair.
        var adjustment = kerning.GetAdjustment(font.MapCodepoint('A'), font.MapCodepoint('V'));

        Assert.True(adjustment < 0, $"Expected negative AV kerning, got {adjustment}.");
    }
}

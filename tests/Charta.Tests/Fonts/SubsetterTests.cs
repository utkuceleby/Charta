using Charta.Fonts;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Fonts;

public class SubsetterTests
{
    [Fact]
    public void Closure_PullsCompositeComponents()
    {
        var font = SfntFont.Parse(SyntheticFont.Build());

        var closure = GlyphClosure.Compute(font, [(ushort)3]);

        Assert.Contains((ushort)0, closure); // .notdef always kept
        Assert.Contains((ushort)1, closure); // component of the composite
        Assert.Contains((ushort)3, closure);
        Assert.DoesNotContain((ushort)2, closure);
    }

    [Fact]
    public void Subset_KeepsRequestedGlyphs_EmptiesTheRest()
    {
        var font = SfntFont.Parse(SyntheticFont.Build());
        var closure = GlyphClosure.Compute(font, [(ushort)3]);

        var subsetBytes = TrueTypeSubsetter.CreateSubset(font, closure);
        var subset = SfntFont.Parse(subsetBytes);

        Assert.Equal(font.NumGlyphs, subset.NumGlyphs); // retain-GID: count unchanged
        Assert.True(subset.GetGlyphData(1).Length > 0);
        Assert.Equal(0, subset.GetGlyphData(2).Length);  // dropped glyph is now empty
        Assert.True(subset.GetGlyphData(3).Length > 0);
        Assert.Equal(font.AdvanceWidth(2), subset.AdvanceWidth(2)); // hmtx copied verbatim
    }

    [Fact]
    public void Subset_GlyphBytesAreIdenticalToOriginal()
    {
        var font = SfntFont.Parse(SyntheticFont.Build());
        var closure = GlyphClosure.Compute(font, [(ushort)1, (ushort)2, (ushort)3]);

        var subset = SfntFont.Parse(TrueTypeSubsetter.CreateSubset(font, closure));

        for (ushort gid = 0; gid < font.NumGlyphs; gid++)
        {
            Assert.Equal(font.GetGlyphData(gid).ToArray(), subset.GetGlyphData(gid).ToArray());
        }
    }

    [Fact]
    public void Subset_WholeFileChecksumIsValid()
    {
        var font = SfntFont.Parse(SyntheticFont.Build());
        var closure = GlyphClosure.Compute(font, [(ushort)1]);

        var subsetBytes = TrueTypeSubsetter.CreateSubset(font, closure);

        // With checkSumAdjustment in place, the whole file must sum to the spec constant.
        Assert.Equal(0xB1B0AFBAu, SfntAssembler.Checksum(subsetBytes));
    }

    [Fact]
    public void Subset_HasNoCmap()
    {
        var font = SfntFont.Parse(SyntheticFont.Build());
        var subset = SfntFont.Parse(TrueTypeSubsetter.CreateSubset(font, GlyphClosure.Compute(font, [(ushort)1])));

        Assert.False(subset.TryGetTable("cmap", out _));
        Assert.Equal(0, subset.MapCodepoint('A')); // degrades to .notdef instead of failing
    }
}

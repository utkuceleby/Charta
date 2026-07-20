using Charta.FontDiscovery;
using Charta.Smoke;
using Xunit;

namespace Charta.Tests.Fonts;

public class FontRegistryTests
{
    [Fact]
    public void Scanner_ReadsFamilyStyleAndOutlineFlavor()
    {
        var faces = FontScanner.Scan(SyntheticFont.Build());

        var face = Assert.Single(faces);
        Assert.Equal("Charta Test", face.FamilyName);
        Assert.Equal("ChartaTest", face.PostScriptName);
        Assert.False(face.IsBold);
        Assert.False(face.IsItalic);
        Assert.Equal(400, face.Weight);
        Assert.True(face.HasTrueTypeOutlines);
    }

    [Fact]
    public void Scanner_ReadsWeightClassFromOs2()
    {
        var face = Assert.Single(FontScanner.Scan(SyntheticFont.Build(weightClass: 600)));

        Assert.Equal(600, face.Weight);
    }

    [Fact]
    public void Scanner_ReturnsEmptyForGarbage()
    {
        Assert.Empty(FontScanner.Scan(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        Assert.Empty(FontScanner.Scan(Array.Empty<byte>()));
    }

    [Fact]
    public void Registry_ResolvesRegisteredFamily_CaseInsensitive()
    {
        var registry = new FontRegistry(useSystemFonts: false);
        registry.Register(SyntheticFont.Build());

        var face = registry.Resolve("charta test");

        Assert.NotNull(face);
        Assert.Equal("ChartaTest", face.PostScriptName);
        Assert.False(face.Load().IsEmpty);
    }

    [Fact]
    public void Registry_ReturnsNullForUnknownFamily()
    {
        var registry = new FontRegistry(useSystemFonts: false);
        registry.Register(SyntheticFont.Build());

        Assert.Null(registry.Resolve("No Such Family"));
    }

    [Fact]
    public void Registry_PrefersStyleMatch_FallsBackAcrossStyles()
    {
        var registry = new FontRegistry(useSystemFonts: false);
        registry.Register(SyntheticFont.Build());

        // Only a regular face exists; a bold request still resolves to it rather than failing.
        var face = registry.Resolve("Charta Test", weight: 700);

        Assert.NotNull(face);
        Assert.False(face.IsBold);
    }

    [Fact]
    public void Registry_ResolvesSemiBold_WhenThatWeightIsRegistered()
    {
        var registry = new FontRegistry(useSystemFonts: false);
        registry.Register(SyntheticFont.Build());                     // weight 400
        registry.Register(SyntheticFont.Build(weightClass: 600));     // weight 600
        registry.Register(SyntheticFont.Build(weightClass: 700));     // weight 700

        var semiBold = registry.Resolve("Charta Test", weight: 600);

        Assert.NotNull(semiBold);
        Assert.Equal(600, semiBold.Weight);
    }

    [Fact]
    public void Registry_FallsBackToNearestWeight_WhenSemiBoldMissing()
    {
        var registry = new FontRegistry(useSystemFonts: false);
        registry.Register(SyntheticFont.Build());                     // weight 400
        registry.Register(SyntheticFont.Build(weightClass: 700));     // weight 700

        // No 600 face exists; 700 is nearer to 600 than 400, so it wins.
        var face = registry.Resolve("Charta Test", weight: 600);

        Assert.NotNull(face);
        Assert.Equal(700, face.Weight);
    }

    [Fact]
    public void Registry_FindsSystemFontsWhenAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var registry = new FontRegistry();

        var regular = registry.Resolve("Arial");
        var bold = registry.Resolve("Arial", weight: 700);

        Assert.NotNull(regular);
        Assert.False(regular.IsBold);
        Assert.NotNull(bold);
        Assert.True(bold.IsBold);
        Assert.NotNull(regular.Path);
    }
}

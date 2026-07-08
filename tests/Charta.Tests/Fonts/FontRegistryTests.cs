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
        Assert.True(face.HasTrueTypeOutlines);
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
        var face = registry.Resolve("Charta Test", bold: true);

        Assert.NotNull(face);
        Assert.False(face.IsBold);
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
        var bold = registry.Resolve("Arial", bold: true);

        Assert.NotNull(regular);
        Assert.False(regular.IsBold);
        Assert.NotNull(bold);
        Assert.True(bold.IsBold);
        Assert.NotNull(regular.Path);
    }
}

using Charta.FontDiscovery;
using Charta.Fonts;
using Xunit;

namespace Charta.Tests.Fonts;

/// <summary>
/// The Linux side of font discovery. The fontconfig parser is tested everywhere via fixtures;
/// the real-font tests run only where DejaVu is installed (the Linux CI/container leg).
/// </summary>
public class LinuxFontTests
{
    [Fact]
    public void FontconfigParser_ReadsDirsIncludesAndPrefixes()
    {
        var root = Directory.CreateTempSubdirectory("charta-fontconfig");
        try
        {
            var confD = Path.Combine(root.FullName, "conf.d");
            Directory.CreateDirectory(confD);

            File.WriteAllText(Path.Combine(root.FullName, "fonts.conf"), $"""
                <?xml version="1.0"?>
                <fontconfig>
                  <dir>/usr/share/fonts</dir>
                  <dir prefix="xdg">fonts</dir>
                  <include ignore_missing="yes">{confD}</include>
                </fontconfig>
                """);
            File.WriteAllText(Path.Combine(confD, "10-extra.conf"), """
                <?xml version="1.0"?>
                <fontconfig>
                  <dir>/opt/myfonts</dir>
                </fontconfig>
                """);

            var directories = new List<string>();
            SystemFontDirectories.CollectFontconfigDirectories(directories, Path.Combine(root.FullName, "fonts.conf"));

            Assert.Contains("/usr/share/fonts", directories);
            Assert.Contains("/opt/myfonts", directories); // via <include>
            Assert.Contains(directories, d => d.EndsWith("fonts", StringComparison.Ordinal) && d != "/usr/share/fonts"); // xdg prefix
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void FontconfigParser_MalformedXml_IsIgnored()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "<fontconfig><dir>/usr/share/fonts</fontconfig>"); // broken

            var directories = new List<string>();
            SystemFontDirectories.CollectFontconfigDirectories(directories, file);
            // No throw; partial results acceptable.
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Linux_SystemDiscovery_FindsDejaVuSans()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var registry = new FontRegistry();
        var face = registry.Resolve("DejaVu Sans");

        Assert.NotNull(face);
        Assert.True(face.HasTrueTypeOutlines);
    }

    [Fact]
    public void Linux_FullPipeline_WithDejaVu()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var registry = new FontRegistry();
        var face = registry.Resolve("DejaVu Sans");
        if (face is null)
        {
            return;
        }

        var font = PdfFont.FromParsed(face.GetParsedFont());
        var shaped = font.Shape("İstanbul ğüşöç Привет Γειά");

        Assert.DoesNotContain(shaped.Glyphs, g => g.GlyphId == 0); // DejaVu covers all of these
        Assert.True(shaped.Width > 0);
    }
}

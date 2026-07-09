using Charta.FontDiscovery;

namespace Charta;

/// <summary>
/// Global font registration. Registering fonts explicitly is the recommended path — it makes output
/// reproducible across machines and containers. When a family is not registered, Charta falls back
/// to scanning the operating system's font directories.
/// </summary>
public static class FontManager
{
    private static readonly FontRegistry Registry = new();

    /// <summary>Registers every face in the given font data (TTF or TTC).</summary>
    public static void RegisterFont(byte[] fontData)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        Registry.Register(fontData);
    }

    /// <summary>Registers every face in the given font stream (TTF or TTC).</summary>
    public static void RegisterFont(Stream fontStream)
    {
        ArgumentNullException.ThrowIfNull(fontStream);
        using var buffer = new MemoryStream();
        fontStream.CopyTo(buffer);
        Registry.Register(buffer.ToArray());
    }

    /// <summary>Registers every face in the given font file (TTF or TTC).</summary>
    public static void RegisterFontFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Registry.Register(path);
    }

    internal static FontFace? Resolve(string familyName, bool bold, bool italic) =>
        Registry.Resolve(familyName, bold, italic);

    internal static FontFace? ResolveDefault(bool bold, bool italic) =>
        Registry.ResolveAnyRegistered(bold, italic);
}

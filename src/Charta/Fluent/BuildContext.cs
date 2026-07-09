using Charta.FontDiscovery;
using Charta.Fonts;
using Charta.Imaging;

namespace Charta.Fluent;

/// <summary>
/// Per-generation state for turning the fluent description into an element tree. PdfFont instances
/// track glyph usage per document, so they are cached here — one per face per generation — and
/// images are deduplicated so a logo repeated on every page embeds once.
/// </summary>
internal sealed class BuildContext
{
    private static readonly string[] DefaultFamilies =
        ["Segoe UI", "Arial", "Helvetica Neue", "Helvetica", "DejaVu Sans", "Liberation Sans", "Noto Sans"];

    private readonly Dictionary<FontFace, PdfFont> _fonts = [];
    private readonly Dictionary<object, PdfImage> _images = [];
    private FontChain? _defaultChain;

    public FontChain ResolveFonts(string? familyName, bool bold, bool italic)
    {
        if (familyName is null && !bold && !italic && _defaultChain is not null)
        {
            return _defaultChain;
        }

        FontFace? face = null;
        if (familyName is not null)
        {
            face = FontManager.Resolve(familyName, bold, italic);
            if (face is null)
            {
                throw new InvalidOperationException(
                    $"Font family '{familyName}' was not found. Register it with FontManager.RegisterFont, or use a family installed on this system.");
            }
        }
        else
        {
            // Registered fonts win the default slot: explicit registration is the reproducible path.
            face = FontManager.ResolveDefault(bold, italic);
            if (face is null)
            {
                foreach (var candidate in DefaultFamilies)
                {
                    face = FontManager.Resolve(candidate, bold, italic);
                    if (face is not null)
                    {
                        break;
                    }
                }
            }

            if (face is null)
            {
                throw new InvalidOperationException(
                    "No usable default font was found on this system. Register one via FontManager.RegisterFont, or set an explicit installed family with .FontFamily(...).");
            }
        }

        var chain = new FontChain(GetFont(face));
        if (familyName is null && !bold && !italic)
        {
            _defaultChain = chain;
        }

        return chain;
    }

    private PdfFont GetFont(FontFace face)
    {
        if (!_fonts.TryGetValue(face, out var font))
        {
            font = PdfFont.Parse(face.Load(), face.CollectionIndex);
            _fonts[face] = font;
        }

        return font;
    }

    /// <summary>Returns one PdfImage per distinct source, so repeated placements share the XObject.</summary>
    public PdfImage GetImage(object key, ReadOnlyMemory<byte> data)
    {
        if (!_images.TryGetValue(key, out var image))
        {
            image = PdfImage.FromBytes(data);
            _images[key] = image;
        }

        return image;
    }
}

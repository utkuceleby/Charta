using Charta.Cos;
using Charta.Fonts;
using Charta.Imaging;

namespace Charta.Layout;

/// <summary>
/// Document-wide resource registry. Fonts and images get stable names (/F1, /Im1 …) and pre-allocated
/// object numbers so page content can reference them immediately; the objects themselves are written
/// later — images at first use, fonts in the trailer phase once glyph usage is final (subsetting).
/// </summary>
internal sealed class PageResources
{
    private readonly Dictionary<PdfFont, (string Name, CosReference Reference)> _fonts = [];
    private readonly Dictionary<PdfImage, (string Name, CosReference Reference, bool Written)> _images = [];
    private readonly PdfWriter _writer;

    public PageResources(PdfWriter writer) => _writer = writer;

    public string GetFontName(PdfFont font)
    {
        if (!_fonts.TryGetValue(font, out var entry))
        {
            entry = ($"F{_fonts.Count + 1}", _writer.Allocate());
            _fonts[font] = entry;
        }

        return entry.Name;
    }

    public string GetImageName(PdfImage image)
    {
        if (!_images.TryGetValue(image, out var entry))
        {
            entry = ($"Im{_images.Count + 1}", _writer.Allocate(), false);
            _images[image] = entry;
        }

        return entry.Name;
    }

    /// <summary>Writes image objects that appeared since the last call (streaming: images go out with their page).</summary>
    public void FlushPendingImages()
    {
        foreach (var (image, entry) in _images.ToList())
        {
            if (!entry.Written)
            {
                image.Write(_writer, entry.Reference);
                _images[image] = entry with { Written = true };
            }
        }
    }

    /// <summary>Writes all font objects. Must run last: subsets depend on every glyph the document used.</summary>
    public void WriteFonts()
    {
        foreach (var (font, entry) in _fonts)
        {
            font.Write(_writer, entry.Reference);
        }
    }

    /// <summary>The shared /Resources dictionary referenced by every page.</summary>
    public CosDictionary BuildResourceDictionary()
    {
        var resources = new CosDictionary();
        if (_fonts.Count > 0)
        {
            var fonts = new CosDictionary();
            foreach (var (_, entry) in _fonts)
            {
                fonts[CosName.Get(entry.Name)] = entry.Reference;
            }

            resources[CosNames.Font] = fonts;
        }

        if (_images.Count > 0)
        {
            var xObjects = new CosDictionary();
            foreach (var (_, entry) in _images)
            {
                xObjects[CosName.Get(entry.Name)] = entry.Reference;
            }

            resources[CosNames.XObject] = xObjects;
        }

        return resources;
    }
}

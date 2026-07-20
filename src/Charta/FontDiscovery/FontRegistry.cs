namespace Charta.FontDiscovery;

/// <summary>
/// Resolves family names to font faces. Explicitly registered fonts always win — the documented,
/// reproducible path for servers and containers. System discovery is the interactive fallback and
/// scans lazily on first use.
/// </summary>
internal sealed class FontRegistry
{
    private static readonly string[] FontExtensions = [".ttf", ".ttc", ".otf"];

    private readonly List<FontFace> _registered = [];
    private readonly object _gate = new();
    private List<FontFace>? _system;
    private readonly bool _useSystemFonts;

    public FontRegistry(bool useSystemFonts = true) => _useSystemFonts = useSystemFonts;

    /// <summary>Registers every face in the given font data. Returns the faces found.</summary>
    public IReadOnlyList<FontFace> Register(ReadOnlyMemory<byte> fontData)
    {
        var faces = FontScanner.Scan(fontData);
        lock (_gate)
        {
            _registered.AddRange(faces);
        }

        return faces;
    }

    public IReadOnlyList<FontFace> Register(string path)
    {
        var faces = FontScanner.Scan(File.ReadAllBytes(path), path);
        lock (_gate)
        {
            _registered.AddRange(faces);
        }

        return faces;
    }

    /// <summary>
    /// Finds the best face for a family: exact family match, then nearest weight and italic agreement.
    /// Registered faces are searched before system faces. Returns null when nothing matches —
    /// the caller decides the fallback policy (diagnostic, chain, or error).
    /// </summary>
    public FontFace? Resolve(string familyName, int weight = 400, bool italic = false)
    {
        lock (_gate)
        {
            return ResolveIn(_registered, familyName, weight, italic)
                ?? (_useSystemFonts ? ResolveIn(SystemFaces(), familyName, weight, italic) : null);
        }
    }

    /// <summary>Best registered face regardless of family — the default when no family was requested.</summary>
    public FontFace? ResolveAnyRegistered(int weight = 400, bool italic = false)
    {
        lock (_gate)
        {
            FontFace? best = null;
            var bestScore = int.MinValue;
            foreach (var face in _registered)
            {
                if (!face.HasTrueTypeOutlines)
                {
                    continue;
                }

                var score = Score(face, weight, italic);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = face;
                }
            }

            return best;
        }
    }

    private static FontFace? ResolveIn(List<FontFace> faces, string familyName, int weight, bool italic)
    {
        FontFace? best = null;
        var bestScore = int.MinValue;
        foreach (var face in faces)
        {
            if (!face.HasTrueTypeOutlines || !string.Equals(face.FamilyName, familyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var score = Score(face, weight, italic);
            if (score > bestScore)
            {
                bestScore = score;
                best = face;
            }
        }

        return best;
    }

    /// <summary>
    /// Nearest weight wins; italic agreement breaks ties. The ×8 keeps weight the primary axis so
    /// even a one-point weight difference outranks an italic match — matching CSS's nearest-weight rule.
    /// </summary>
    private static int Score(FontFace face, int weight, bool italic) =>
        (face.IsItalic == italic ? 1 : 0) - (Math.Abs(face.Weight - weight) * 8);

    private List<FontFace> SystemFaces()
    {
        if (_system is not null)
        {
            return _system;
        }

        var faces = new List<FontFace>();
        foreach (var directory in SystemFontDirectories.Get())
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!FontExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    faces.AddRange(FontScanner.Scan(File.ReadAllBytes(file), file));
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        _system = faces;
        return faces;
    }
}

using System.Globalization;
using System.Text;

// Generates src/Charta/Text/Generated/LineBreakData.g.cs from the Unicode Character Database files
// in ./data. Run manually after a Unicode version bump:
//   dotnet run --project tools/Charta.CodeGen
//
// LB1 class resolution (AI/SG/XX → AL, CJ → NS, SA → CM or AL by general category) happens here,
// at generation time, so the runtime table is already resolved and immune to ICU version drift.

var dataDirectory = args.Length > 0 ? args[0] : Path.Combine(FindRepoRoot(), "tools", "Charta.CodeGen", "data");
var outputPath = args.Length > 1 ? args[1] : Path.Combine(FindRepoRoot(), "src", "Charta", "Text", "Generated", "LineBreakData.g.cs");

// Class names must match the runtime LineBreakClass enum ordinals.
string[] classNames =
[
    "OP", "CL", "CP", "QU", "GL", "NS", "EX", "SY", "IS", "PR", "PO", "NU", "AL", "HL", "ID", "IN",
    "HY", "BA", "BB", "B2", "ZW", "CM", "WJ", "H2", "H3", "JL", "JV", "JT", "RI", "EB", "EM", "ZWJ",
    "CB", "AK", "AP", "AS", "VF", "VI", "BK", "CR", "LF", "NL", "SP",
];
var classIndex = classNames.Select((name, i) => (name, i)).ToDictionary(p => p.name, p => (byte)p.i);

const int MaxCodepoint = 0x110000;
var classes = new byte[MaxCodepoint];
var flags = new byte[MaxCodepoint];
Array.Fill(classes, classIndex["AL"]); // final default after LB1 (XX → AL)

// Track raw values that need general-category-dependent resolution.
var rawSa = new bool[MaxCodepoint];
var generalCategory = new string[MaxCodepoint];

// Pass 1: @missing defaults from LineBreak.txt (ID ranges for unassigned CJK planes, PR for currency).
foreach (var line in File.ReadLines(Path.Combine(dataDirectory, "LineBreak.txt")))
{
    if (line.StartsWith("# @missing:", StringComparison.Ordinal))
    {
        var payload = line["# @missing:".Length..].Trim();
        ApplyRange(payload, value =>
        {
            return value switch
            {
                "Unknown" or "XX" => classIndex["AL"],
                "ID" => classIndex["ID"],
                "PR" => classIndex["PR"],
                _ => classIndex["AL"],
            };
        }, classes);
    }
}

// Pass 2: explicit LineBreak.txt assignments.
foreach (var line in File.ReadLines(Path.Combine(dataDirectory, "LineBreak.txt")))
{
    var (range, value) = ParseLine(line);
    if (range is null)
    {
        continue;
    }

    var (start, end) = range.Value;
    byte resolved;
    var isSa = false;
    switch (value)
    {
        case "AI" or "SG" or "XX":
            resolved = classIndex["AL"];
            break;
        case "CJ":
            resolved = classIndex["NS"];
            break;
        case "SA":
            resolved = classIndex["AL"]; // refined to CM below when GC is Mn/Mc
            isSa = true;
            break;
        default:
            if (!classIndex.TryGetValue(value, out resolved))
            {
                throw new InvalidOperationException($"Unknown line break class '{value}' — update the class list.");
            }

            break;
    }

    for (var cp = start; cp <= end; cp++)
    {
        classes[cp] = resolved;
        rawSa[cp] = isSa;
    }
}

// Pass 3: general categories (SA refinement + Pi/Pf flags + Cn for the ExtPict flag).
foreach (var line in File.ReadLines(Path.Combine(dataDirectory, "DerivedGeneralCategory.txt")))
{
    var (range, value) = ParseLine(line);
    if (range is null)
    {
        continue;
    }

    for (var cp = range.Value.Start; cp <= range.Value.End; cp++)
    {
        generalCategory[cp] = value;
    }
}

for (var cp = 0; cp < MaxCodepoint; cp++)
{
    var gc = generalCategory[cp];
    if (rawSa[cp] && gc is "Mn" or "Mc")
    {
        classes[cp] = classIndex["CM"];
    }

    if (gc == "Pi")
    {
        flags[cp] |= 0x04;
    }
    else if (gc == "Pf")
    {
        flags[cp] |= 0x08;
    }
}

// Pass 4: East Asian width (F/W/H) flag.
foreach (var line in File.ReadLines(Path.Combine(dataDirectory, "EastAsianWidth.txt")))
{
    if (line.StartsWith("# @missing:", StringComparison.Ordinal))
    {
        var payload = line["# @missing:".Length..].Trim();
        ApplyRangeFlag(payload, value => value is "W" or "F" or "H", 0x01, flags);
        continue;
    }

    var (range, value) = ParseLine(line);
    if (range is null || value is not ("W" or "F" or "H"))
    {
        continue;
    }

    for (var cp = range.Value.Start; cp <= range.Value.End; cp++)
    {
        flags[cp] |= 0x01;
    }
}

// Pass 5: Extended_Pictographic ∩ Cn (LB30b's unassigned-emoji clause).
foreach (var line in File.ReadLines(Path.Combine(dataDirectory, "emoji-data.txt")))
{
    var (range, value) = ParseLine(line);
    if (range is null || value != "Extended_Pictographic")
    {
        continue;
    }

    for (var cp = range.Value.Start; cp <= range.Value.End; cp++)
    {
        if (generalCategory[cp] is null or "Cn")
        {
            flags[cp] |= 0x02;
        }
    }
}

// Compress to ranges of identical (class, flags).
var starts = new List<int>();
var payloadClasses = new List<byte>();
var payloadFlags = new List<byte>();
for (var cp = 0; cp < MaxCodepoint; cp++)
{
    if (cp == 0 || classes[cp] != classes[cp - 1] || flags[cp] != flags[cp - 1])
    {
        starts.Add(cp);
        payloadClasses.Add(classes[cp]);
        payloadFlags.Add(flags[cp]);
    }
}

var sb = new StringBuilder();
sb.Append("// <auto-generated>\n");
sb.Append("// Generated by tools/Charta.CodeGen from the Unicode 16.0.0 Character Database.\n");
sb.Append("// Do not edit; re-run the generator after a Unicode version bump.\n");
sb.Append("// </auto-generated>\n\n");
sb.Append("namespace Charta.Text;\n\n");
sb.Append("internal static partial class LineBreakData\n{\n");
sb.Append(CultureInfo.InvariantCulture, $"    public const int RangeCount = {starts.Count};\n\n");

sb.Append("    public static ReadOnlySpan<int> RangeStarts =>\n    [\n");
AppendInts(sb, starts);
sb.Append("    ];\n\n");

sb.Append("    public static ReadOnlySpan<byte> RangeClasses =>\n    [\n");
AppendBytes(sb, payloadClasses);
sb.Append("    ];\n\n");

sb.Append("    public static ReadOnlySpan<byte> RangeFlags =>\n    [\n");
AppendBytes(sb, payloadFlags);
sb.Append("    ];\n}\n");

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, sb.ToString());
Console.WriteLine($"{starts.Count} ranges -> {outputPath}");

static (int Start, int End)? ParseRangeToken(string token)
{
    token = token.Trim();
    if (token.Length == 0)
    {
        return null;
    }

    var dots = token.IndexOf("..", StringComparison.Ordinal);
    if (dots >= 0)
    {
        return (int.Parse(token[..dots], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(token[(dots + 2)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    var single = int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    return (single, single);
}

static ((int Start, int End)? Range, string Value) ParseLine(string line)
{
    var hash = line.IndexOf('#', StringComparison.Ordinal);
    if (hash >= 0)
    {
        line = line[..hash];
    }

    var parts = line.Split(';');
    if (parts.Length < 2)
    {
        return (null, string.Empty);
    }

    return (ParseRangeToken(parts[0]), parts[1].Trim());
}

static void ApplyRange(string payload, Func<string, byte> map, byte[] target)
{
    var parts = payload.Split(';');
    if (parts.Length < 2 || ParseRangeToken(parts[0]) is not { } range)
    {
        return;
    }

    var value = map(parts[1].Trim());
    for (var cp = range.Start; cp <= range.End; cp++)
    {
        target[cp] = value;
    }
}

static void ApplyRangeFlag(string payload, Func<string, bool> predicate, byte flag, byte[] target)
{
    var parts = payload.Split(';');
    if (parts.Length < 2 || ParseRangeToken(parts[0]) is not { } range)
    {
        return;
    }

    if (!predicate(parts[1].Trim()))
    {
        return;
    }

    for (var cp = range.Start; cp <= range.End; cp++)
    {
        target[cp] |= flag;
    }
}

static void AppendInts(StringBuilder sb, List<int> values)
{
    for (var i = 0; i < values.Count; i++)
    {
        if (i % 16 == 0)
        {
            sb.Append("        ");
        }

        sb.Append("0x").Append(values[i].ToString("X", CultureInfo.InvariantCulture)).Append(',');
        sb.Append(i % 16 == 15 ? '\n' : ' ');
    }

    if (values.Count % 16 != 0)
    {
        sb.Append('\n');
    }
}

static void AppendBytes(StringBuilder sb, List<byte> values)
{
    for (var i = 0; i < values.Count; i++)
    {
        if (i % 24 == 0)
        {
            sb.Append("        ");
        }

        sb.Append(values[i].ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(i % 24 == 23 ? '\n' : ' ');
    }

    if (values.Count % 24 != 0)
    {
        sb.Append('\n');
    }
}

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir is not null && !File.Exists(Path.Combine(dir, "Charta.slnx")))
    {
        dir = Path.GetDirectoryName(dir);
    }

    return dir ?? throw new InvalidOperationException("Repository root not found.");
}

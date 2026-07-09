using Charta.Text;
using Xunit;

namespace Charta.Tests.Text;

/// <summary>
/// The official UAX#9 conformance suites: BidiTest.txt (~490k class-sequence cases across paragraph
/// directions) and BidiCharacterTest.txt (~90k real-codepoint cases including bracket pairing).
/// The suites are a complete oracle — anything below 100% is a bug.
/// </summary>
public class BidiConformanceTests
{
    private static readonly Dictionary<string, BidiClass> ClassByName = Enum
        .GetValues<BidiClass>()
        .ToDictionary(c => c.ToString(), c => c, StringComparer.Ordinal);

    [Fact]
    public void PassesBidiTest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "BidiTest.txt");
        var failures = new List<string>();
        var total = 0;
        var lineNumber = 0;

        byte[]? expectedLevels = null;
        int[]? expectedOrder = null;

        foreach (var rawLine in File.ReadLines(path))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("@Levels:", StringComparison.Ordinal))
            {
                expectedLevels = line["@Levels:".Length..]
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => t == "x" ? BidiAlgorithm.RemovedLevel : byte.Parse(t, System.Globalization.CultureInfo.InvariantCulture))
                    .ToArray();
                continue;
            }

            if (line.StartsWith("@Reorder:", StringComparison.Ordinal))
            {
                expectedOrder = line["@Reorder:".Length..]
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => int.Parse(t, System.Globalization.CultureInfo.InvariantCulture))
                    .ToArray();
                continue;
            }

            var semi = line.IndexOf(';', StringComparison.Ordinal);
            var classes = line[..semi]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => ClassByName[name])
                .ToArray();
            var bitset = int.Parse(line[(semi + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture);

            for (var direction = 0; direction < 3; direction++)
            {
                if ((bitset & (1 << direction)) == 0)
                {
                    continue;
                }

                total++;
                var paragraphLevel = direction switch
                {
                    0 => BidiAlgorithm.ResolveParagraphLevel(classes),
                    1 => (byte)0,
                    _ => (byte)1,
                };

                var levels = BidiAlgorithm.ResolveLevels(classes, [], paragraphLevel);
                var order = BidiAlgorithm.ReorderLine(levels, 0, levels.Length);

                if (!levels.AsSpan().SequenceEqual(expectedLevels) || !order.AsSpan().SequenceEqual(expectedOrder))
                {
                    if (failures.Count < 20)
                    {
                        failures.Add(
                            $"line {lineNumber} dir={direction}: [{line}]\n" +
                            $"    levels expected [{Format(expectedLevels!)}] got [{Format(levels)}]\n" +
                            $"    order expected [{string.Join(' ', expectedOrder!)}] got [{string.Join(' ', order)}]");
                    }
                    else if (failures.Count == 20)
                    {
                        failures.Add("(more...)");
                    }
                }
            }
        }

        Assert.True(total > 400_000, $"Suite looks truncated: {total} cases.");
        Assert.True(failures.Count <= 20, $"many failures; first 20:\n{string.Join('\n', failures)}");
        Assert.True(failures.Count == 0, $"{failures.Count} shown of failing cases (of {total}):\n{string.Join('\n', failures)}");
    }

    [Fact]
    public void PassesBidiCharacterTest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "BidiCharacterTest.txt");
        var failures = new List<string>();
        var total = 0;
        var lineNumber = 0;

        foreach (var rawLine in File.ReadLines(path))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            total++;
            var fields = line.Split(';');
            var codepoints = fields[0]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => int.Parse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            var direction = int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);
            var expectedParagraphLevel = byte.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture);
            var expectedLevels = fields[3]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t == "x" ? BidiAlgorithm.RemovedLevel : byte.Parse(t, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            var expectedOrder = fields[4]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => int.Parse(t, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();

            var classes = codepoints.Select(UnicodeBidi.GetClass).ToArray();
            var paragraphLevel = direction == 2
                ? BidiAlgorithm.ResolveParagraphLevel(classes)
                : (byte)direction;

            var levels = BidiAlgorithm.ResolveLevels(classes, codepoints, paragraphLevel);
            var order = BidiAlgorithm.ReorderLine(levels, 0, levels.Length);

            if (paragraphLevel != expectedParagraphLevel ||
                !levels.AsSpan().SequenceEqual(expectedLevels) ||
                !order.AsSpan().SequenceEqual(expectedOrder))
            {
                if (failures.Count < 20)
                {
                    failures.Add(
                        $"line {lineNumber}: [{line}]\n" +
                        $"    para expected {expectedParagraphLevel} got {paragraphLevel}\n" +
                        $"    levels expected [{Format(expectedLevels)}] got [{Format(levels)}]\n" +
                        $"    order expected [{string.Join(' ', expectedOrder)}] got [{string.Join(' ', order)}]");
                }
            }
        }

        Assert.True(total > 80_000, $"Suite looks truncated: {total} cases.");
        Assert.True(failures.Count == 0, $"failing cases (of {total}):\n{string.Join('\n', failures)}");
    }

    private static string Format(IEnumerable<byte> levels) =>
        string.Join(' ', levels.Select(l => l == BidiAlgorithm.RemovedLevel ? "x" : l.ToString(System.Globalization.CultureInfo.InvariantCulture)));
}

using System.Text;
using Charta.Text;
using Xunit;

namespace Charta.Tests.Text;

/// <summary>
/// Runs the official Unicode LineBreakTest.txt suite (~7,000 cases) against the LineBreaker.
/// The suite is the complete oracle for UAX#14 — anything below 100% is a bug.
/// </summary>
public class LineBreakConformanceTests
{
    [Fact]
    public void PassesOfficialConformanceSuite()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "LineBreakTest.txt");
        var failures = new List<string>();
        var total = 0;
        var lineNumber = 0;

        foreach (var rawLine in File.ReadLines(path))
        {
            lineNumber++;
            var line = rawLine;
            var hash = line.IndexOf('#', StringComparison.Ordinal);
            if (hash >= 0)
            {
                line = line[..hash];
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            total++;
            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Tokens alternate: mark cp mark cp ... mark. Marks: '÷' break allowed, '×' forbidden.
            var text = new StringBuilder();
            var expectedBreaks = new HashSet<int>();
            var charOffset = 0;
            for (var i = 1; i < tokens.Length; i += 2)
            {
                var codepoint = int.Parse(tokens[i], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
                text.Append(char.ConvertFromUtf32(codepoint));
                charOffset += char.ConvertFromUtf32(codepoint).Length;
                if (i + 1 < tokens.Length && tokens[i + 1] == "÷")
                {
                    expectedBreaks.Add(charOffset);
                }
            }

            var input = text.ToString();
            var actualBreaks = new HashSet<int>();
            foreach (var opportunity in LineBreaker.FindBreaks(input))
            {
                actualBreaks.Add(opportunity.Position);
            }

            if (!expectedBreaks.SetEquals(actualBreaks))
            {
                if (failures.Count < 25)
                {
                    var missing = string.Join(",", expectedBreaks.Except(actualBreaks));
                    var extra = string.Join(",", actualBreaks.Except(expectedBreaks));
                    failures.Add($"line {lineNumber}: {rawLine.Trim()}\n    missing breaks at [{missing}], unexpected at [{extra}]");
                }
                else
                {
                    failures.Add("(more...)");
                    break;
                }
            }
        }

        Assert.True(total > 5000, $"Suite looks truncated: only {total} cases parsed.");
        Assert.True(failures.Count == 0, $"{failures.Count} of {total} conformance cases failed:\n" + string.Join('\n', failures));
    }
}

using PublicApiGenerator;
using Xunit;

namespace Charta.Tests.PublicApi;

/// <summary>
/// Locks the public API surface: any change to public types or members fails this test until the
/// approved file is updated — making API changes explicit, reviewable diffs.
/// </summary>
public class ApiApprovalTests
{
    [Fact]
    public void PublicApi_MatchesApprovedSurface()
    {
        var actual = typeof(Document).Assembly.GeneratePublicApi(new ApiGeneratorOptions
        {
            IncludeAssemblyAttributes = false,
        }).ReplaceLineEndings("\n").Trim();

        var approvedPath = Path.Combine(AppContext.BaseDirectory, "PublicApi", "PublicApi.approved.txt");
        var approved = File.Exists(approvedPath)
            ? File.ReadAllText(approvedPath).ReplaceLineEndings("\n").Trim()
            : string.Empty;

        if (!string.Equals(approved, actual, StringComparison.Ordinal))
        {
            var receivedPath = Path.Combine(AppContext.BaseDirectory, "PublicApi.received.txt");
            File.WriteAllText(receivedPath, actual);
            Assert.Fail(
                $"The public API surface changed. The current surface was written to:\n{receivedPath}\n" +
                "If the change is intentional, copy it over tests/Charta.Tests/PublicApi/PublicApi.approved.txt.");
        }
    }
}

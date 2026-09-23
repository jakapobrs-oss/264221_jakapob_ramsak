using System.Diagnostics;

using DiscountModule.Json;

namespace DiscountModule.Cli.Tests;

/// <summary>The CLI prints a bounded number of problems however broken the order is.</summary>
public class ProblemLimitTests
{
    private static (int ExitCode, string[] ErrorLines) Run(string json)
    {
        var stderr = new StringWriter();
        int exitCode = CliApp.Run(["-"], new StringWriter(), stderr, new StringReader(json));
        return (exitCode, stderr.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Many_broken_rules_show_the_first_100_and_count_them_all()
    {
        // 1,000 well-formed items with a negative price: 1,000 broken business rules.
        string items = string.Join(", ", Enumerable.Range(1, 1_000)
            .Select(n => $"{{ \"name\": \"Item {n}\", \"category\": \"Clothing\", \"unitPrice\": -1 }}"));

        (int exitCode, string[] lines) = Run($"{{ \"items\": [{items}] }}");

        Assert.Equal(CliApp.ExitValidationError, exitCode);
        Assert.Equal("Invalid order (1000):", lines[0]);
        Assert.Equal(1 + ProblemList.MaxReportedProblems + 1, lines.Length);
        Assert.Equal("  - Item #100 'Item 100': unit price must not be negative (got -1)", lines[100]);
        Assert.Equal("  - ... and 900 more problems not shown", lines[^1]);
    }

    [Fact]
    public void A_huge_broken_file_is_reported_quickly_with_the_first_100_problems()
    {
        // Just under 1 MiB of empty items. Before the limit this printed about a million lines.
        string json = "{ \"items\": [" + string.Join(", ", Enumerable.Repeat("{}", 349_000)) + "] }";

        var stopwatch = Stopwatch.StartNew();
        (int exitCode, string[] lines) = Run(json);
        stopwatch.Stop();

        Assert.Equal(CliApp.ExitInputError, exitCode);
        Assert.Equal("Input error (more than 100):", lines[0]);
        Assert.Equal(1 + ProblemList.MaxReportedProblems + 1, lines.Length);
        Assert.Equal("  - Stopped after 100 problems; fix these first", lines[^1]);

        // Generous on purpose so that a slow CI machine stays green; the work is now a fraction of a second.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"took {stopwatch.Elapsed}");
    }
}

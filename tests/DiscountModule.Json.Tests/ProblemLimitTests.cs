namespace DiscountModule.Json.Tests;

/// <summary>A huge broken order is reported with a bounded number of messages (see <see cref="ProblemList"/>).</summary>
public class ProblemLimitTests
{
    /// <summary>Upper bound for parsing just under 1 MiB of broken items; measured cost with the limit is well below it.</summary>
    private const long AllocationBudgetBytes = 64L * 1024 * 1024;

    /// <summary>An order whose items are empty objects: 3 problems each (name, category and unitPrice are missing).</summary>
    private static string EmptyItems(int count)
        => "{ \"items\": [" + string.Join(", ", Enumerable.Repeat("{}", count)) + "] }";

    /// <summary>An order whose items only lack a unitPrice: exactly 1 problem each.</summary>
    private static string ItemsWithoutPrice(int count)
        => "{ \"items\": [" + string.Join(", ", Enumerable.Range(1, count).Select(n => $"{{ \"name\": \"Item {n}\", \"category\": \"Clothing\" }}")) + "] }";

    [Fact]
    public void A_huge_broken_order_reports_the_first_100_problems_and_says_it_stopped()
    {
        InputFormatException error = Assert.Throws<InputFormatException>(() => OrderRequestParser.Parse(EmptyItems(1_000)));

        Assert.Equal(ProblemList.MaxReportedProblems + 1, error.Errors.Count);
        Assert.Equal("$.items[0]: \"name\" is required", error.Errors[0]);
        Assert.Equal("Stopped after 100 problems; fix these first", error.Errors[^1]);
    }

    [Fact]
    public void Exactly_100_problems_are_all_reported_without_a_closing_line()
    {
        InputFormatException error = Assert.Throws<InputFormatException>(() => OrderRequestParser.Parse(ItemsWithoutPrice(100)));

        Assert.Equal(100, error.Errors.Count);
        Assert.Equal("$.items[99]: \"unitPrice\" is required", error.Errors[^1]);
    }

    [Fact]
    public void One_problem_over_the_limit_is_cut_and_announced()
    {
        InputFormatException error = Assert.Throws<InputFormatException>(() => OrderRequestParser.Parse(ItemsWithoutPrice(101)));

        Assert.Equal(101, error.Errors.Count);
        Assert.Equal("$.items[99]: \"unitPrice\" is required", error.Errors[99]);
        Assert.Equal(ProblemList.StoppedMessage, error.Errors[100]);
    }

    [Fact]
    public void The_parser_stops_reading_instead_of_building_a_huge_list()
    {
        // Just under 1 MiB of broken items used to produce about a million messages (tens of megabytes).
        string json = EmptyItems(349_000);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InputFormatException>(() => OrderRequestParser.Parse(json));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < AllocationBudgetBytes, $"parsing allocated {allocated:N0} bytes");
    }

    [Fact]
    public void For_display_keeps_short_lists_and_cuts_long_ones_with_a_count()
    {
        string[] hundred = [.. Enumerable.Range(1, 100).Select(n => $"problem {n}")];
        string[] hundredAndOne = [.. Enumerable.Range(1, 101).Select(n => $"problem {n}")];
        string[] thousand = [.. Enumerable.Range(1, 1_000).Select(n => $"problem {n}")];

        Assert.Equal(hundred, ProblemList.ForDisplay(hundred));
        Assert.Equal("... and 1 more problem not shown", ProblemList.ForDisplay(hundredAndOne)[^1]);

        IReadOnlyList<string> shown = ProblemList.ForDisplay(thousand);
        Assert.Equal(101, shown.Count);
        Assert.Equal("problem 100", shown[99]);
        Assert.Equal("... and 900 more problems not shown", shown[100]);
    }
}

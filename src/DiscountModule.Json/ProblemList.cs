using static System.FormattableString;

namespace DiscountModule.Json;

/// <summary>
/// Caps how many problems are reported for one order. A 1 MiB order made of broken items would otherwise
/// produce about a million messages (tens of megabytes of text) that a front end has to hold in memory and
/// send back. The CLI and the HTTP API share this limit.
/// </summary>
public static class ProblemList
{
    /// <summary>Most problems reported for one order.</summary>
    public const int MaxReportedProblems = 100;

    /// <summary>Closing line when the parser stopped reading an order because it had too many problems.</summary>
    public static string StoppedMessage { get; } = Invariant($"Stopped after {MaxReportedProblems} problems; fix these first");

    /// <summary>
    /// The problems to show: all of them when there are at most <see cref="MaxReportedProblems"/>, otherwise
    /// the first <see cref="MaxReportedProblems"/> and one line saying how many were left out.
    /// </summary>
    public static IReadOnlyList<string> ForDisplay(IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        if (problems.Count <= MaxReportedProblems)
        {
            return problems;
        }

        int hidden = problems.Count - MaxReportedProblems;
        string noun = hidden == 1 ? "problem" : "problems";
        return [.. problems.Take(MaxReportedProblems), Invariant($"... and {hidden} more {noun} not shown")];
    }
}

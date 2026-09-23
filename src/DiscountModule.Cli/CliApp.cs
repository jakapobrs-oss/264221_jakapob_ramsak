using DiscountModule.Cli.Output;
using DiscountModule.Json;
using DiscountModule.Validation;

using static System.FormattableString;

namespace DiscountModule.Cli;

/// <summary>
/// Command line front end of the discount module: read a JSON order, apply the campaigns, print the result.
/// Kept free of <see cref="Console"/> so that tests can drive it with in-memory readers and writers.
/// </summary>
public static class CliApp
{
    /// <summary>The order was priced successfully.</summary>
    public const int ExitSuccess = 0;

    /// <summary>The input was well-formed but breaks a discount rule (e.g. two coupons, a negative price).</summary>
    public const int ExitValidationError = 1;

    /// <summary>The input could not be read or understood (missing file, malformed JSON, unknown campaign type, bad usage).</summary>
    public const int ExitInputError = 2;

    /// <summary>Something failed that the program does not expect (a bug, or e.g. a closed output pipe).</summary>
    public const int ExitUnexpectedError = 3;

    private const string StdinMarker = "-";
    private const string EndOfOptions = "--";

    private const string Usage = """
        Usage: discount [--json] <order.json>
               discount [--json] -          read the order from standard input

        Options:
          --json        print the result as JSON instead of a text breakdown
          -h, --help    show this help
          --            treat every later argument as a file name, even if it starts with '-'

        Exit codes: 0 = success, 1 = the order breaks a discount rule, 2 = the input could not be read,
                    3 = unexpected error.
        Input format: see README.md or any file in examples/.
        """;

    /// <summary>
    /// Like <see cref="Run"/>, but an unexpected exception becomes one line on stderr and
    /// <see cref="ExitUnexpectedError"/> instead of a stack trace. Program.cs uses this; tests call
    /// <see cref="Run"/> directly so that a bug still surfaces as an exception.
    /// </summary>
    public static int RunSafely(string[] args, TextWriter stdout, TextWriter stderr, TextReader? stdin = null)
    {
        try
        {
            return Run(args, stdout, stderr, stdin);
        }
        catch (Exception ex)
        {
            try
            {
                stderr.WriteLine(ConsoleText.Escape($"Unexpected error: {ex.GetType().Name}: {ex.Message}"));
            }
            catch (IOException)
            {
                // stderr is gone too (e.g. both pipes closed); the exit code is all that is left to report.
            }

            return ExitUnexpectedError;
        }
    }

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, TextReader? stdin = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        string? source = null;
        bool asJson = false;
        bool optionsEnded = false;

        foreach (string arg in args)
        {
            bool isOption = !optionsEnded && arg.StartsWith('-') && arg != StdinMarker;
            if (isOption)
            {
                switch (arg)
                {
                    case EndOfOptions:
                        optionsEnded = true;
                        continue;
                    case "--json":
                        asJson = true;
                        continue;
                    case "-h" or "--help":
                        stdout.WriteLine(Usage);
                        return ExitSuccess;
                    default:
                        return UsageError(stderr, ConsoleText.Escape($"Unknown option '{arg}'"));
                }
            }

            if (source is not null)
            {
                return UsageError(stderr, "Only one order file can be given");
            }

            source = arg;
        }

        if (source is null)
        {
            return UsageError(stderr, problem: null);
        }

        try
        {
            OrderRequest request = source == StdinMarker
                ? OrderRequestParser.Read(stdin ?? Console.In)
                : OrderRequestParser.ReadFile(source);

            DiscountResult result = DiscountCalculator.Calculate(request.Cart, request.Campaigns);

            if (asJson)
            {
                ResultPrinter.WriteJson(result, stdout);
            }
            else
            {
                ResultPrinter.WriteText(request.Cart, result, stdout);
            }

            return ExitSuccess;
        }
        catch (InputFormatException ex)
        {
            // The parser stops after MaxReportedProblems and adds a closing line, so past that the total is unknown.
            string count = ex.Errors.Count > ProblemList.MaxReportedProblems
                ? Invariant($"more than {ProblemList.MaxReportedProblems}")
                : Invariant($"{ex.Errors.Count}");
            Report(stderr, $"Input error ({count})", ex.Errors);
            return ExitInputError;
        }
        catch (DiscountValidationException ex)
        {
            // The heading counts every broken rule; the list shows at most MaxReportedProblems of them.
            Report(stderr, Invariant($"Invalid order ({ex.Errors.Count})"), ProblemList.ForDisplay(ex.Errors));
            return ExitValidationError;
        }
    }

    private static int UsageError(TextWriter stderr, string? problem)
    {
        if (problem is not null)
        {
            stderr.WriteLine(problem);
        }

        stderr.WriteLine(Usage);
        return ExitInputError;
    }

    private static void Report(TextWriter stderr, string heading, IReadOnlyList<string> errors)
    {
        stderr.WriteLine($"{heading}:");
        foreach (string error in errors)
        {
            // Messages can quote input text (an item name, an unknown value); escaping keeps one problem per line.
            stderr.WriteLine($"  - {ConsoleText.Escape(error)}");
        }
    }
}

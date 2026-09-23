using DiscountModule.Cli.Input;
using DiscountModule.Cli.Output;
using DiscountModule.Validation;

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

    /// <summary>Something failed that the program does not expect; see Program.cs.</summary>
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
                        return UsageError(stderr, $"Unknown option '{arg}'");
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
            Report(stderr, "Input error", ex.Errors);
            return ExitInputError;
        }
        catch (DiscountValidationException ex)
        {
            Report(stderr, "Invalid order", ex.Errors);
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

    private static void Report(TextWriter stderr, string title, IReadOnlyList<string> errors)
    {
        stderr.WriteLine($"{title} ({errors.Count}):");
        foreach (string error in errors)
        {
            stderr.WriteLine($"  - {error}");
        }
    }
}

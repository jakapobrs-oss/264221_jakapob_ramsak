using DiscountModule.Cli.Input;
using DiscountModule.Cli.Output;
using DiscountModule.Validation;

namespace DiscountModule.Cli;

/// <summary>
/// Command line front end of the discount module: read a JSON order, apply the campaigns, print the result.
/// Kept free of <see cref="Console"/> so that tests can drive it with in-memory writers.
/// </summary>
public static class CliApp
{
    /// <summary>The order was priced successfully.</summary>
    public const int ExitSuccess = 0;

    /// <summary>The input was well-formed but violates a discount rule (e.g. two coupons, negative price).</summary>
    public const int ExitValidationError = 1;

    /// <summary>The input could not be read or understood (missing file, malformed JSON, unknown campaign type, bad usage).</summary>
    public const int ExitInputError = 2;

    private const string StdinMarker = "-";

    private const string Usage = """
        Usage: discount <order.json> [--json]
               discount - [--json]          read the order from standard input

        Options:
          --json        print the result as JSON instead of a text breakdown
          -h, --help    show this help

        Exit codes: 0 = success, 1 = the order violates a discount rule, 2 = the input could not be read.
        Input format: docs/DESIGN.md section 7, or any file in examples/.
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, TextReader? stdin = null)
    {
        string? source = null;
        var asJson = false;

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--json":
                    asJson = true;
                    break;
                case "-h" or "--help":
                    stdout.WriteLine(Usage);
                    return ExitSuccess;
                case var option when option.StartsWith('-') && option != StdinMarker:
                    stderr.WriteLine($"Unknown option '{option}'");
                    stderr.WriteLine(Usage);
                    return ExitInputError;
                default:
                    if (source is not null)
                    {
                        stderr.WriteLine("Only one order file can be given");
                        stderr.WriteLine(Usage);
                        return ExitInputError;
                    }

                    source = arg;
                    break;
            }
        }

        if (source is null)
        {
            stderr.WriteLine(Usage);
            return ExitInputError;
        }

        try
        {
            var dto = source == StdinMarker
                ? OrderRequestReader.Parse((stdin ?? Console.In).ReadToEnd())
                : OrderRequestReader.ReadFile(source);

            var request = OrderRequestMapper.Map(dto);
            var result = DiscountCalculator.Calculate(request.Cart, request.Campaigns);

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

    private static void Report(TextWriter stderr, string title, IReadOnlyList<string> errors)
    {
        stderr.WriteLine($"{title} ({errors.Count}):");
        foreach (var error in errors)
        {
            stderr.WriteLine($"  - {error}");
        }
    }
}

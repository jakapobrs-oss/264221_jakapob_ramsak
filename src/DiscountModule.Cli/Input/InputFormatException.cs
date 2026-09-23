namespace DiscountModule.Cli.Input;

/// <summary>
/// The input file could not be turned into a cart and campaign list: unreadable file, malformed JSON,
/// unknown campaign type or category, missing or unexpected parameters. Business-rule violations
/// (negative price, two coupons, ...) are not format errors; the module reports those separately.
/// </summary>
public sealed class InputFormatException : Exception
{
    public InputFormatException(IReadOnlyList<string> errors)
        : base("Invalid input: " + string.Join("; ", errors))
    {
        Errors = errors;
    }

    public InputFormatException(string error)
        : this([error])
    {
    }

    public IReadOnlyList<string> Errors { get; }
}

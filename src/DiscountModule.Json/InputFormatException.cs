namespace DiscountModule.Json;

/// <summary>
/// The input could not be turned into a cart and a campaign list: the file cannot be read, the JSON is
/// malformed, or the document has the wrong shape (unknown campaign type or category, a missing or
/// unexpected property, a value of the wrong type). Breaking a business rule, such as a negative price
/// or two coupons, is not a format error; the module reports those separately.
/// </summary>
public sealed class InputFormatException : Exception
{
    public InputFormatException(IEnumerable<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        // Copy, so later changes to the caller's list cannot change what this exception reports.
        Errors = Array.AsReadOnly(errors.ToArray());
    }

    public InputFormatException(string error)
        : this([error])
    {
    }

    public IReadOnlyList<string> Errors { get; }

    // Built from the same copy as Errors, so the two can never disagree.
    public override string Message => "Invalid input: " + string.Join("; ", Errors);
}

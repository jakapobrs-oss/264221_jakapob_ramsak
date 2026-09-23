namespace DiscountModule.Validation;

/// <summary>Thrown when the cart or the campaign list is invalid. Carries every problem found, not just the first.</summary>
public sealed class DiscountValidationException : Exception
{
    public DiscountValidationException(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        // Copy, so later changes to the caller's list cannot change what this exception reports.
        Errors = Array.AsReadOnly(errors.ToArray());
    }

    public IReadOnlyList<string> Errors { get; }

    // Built from the same copy as Errors, so the two can never disagree.
    public override string Message => "Invalid discount request: " + string.Join("; ", Errors);
}

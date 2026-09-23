namespace DiscountModule.Validation;

/// <summary>Thrown when the cart or the campaign list is invalid. Carries every problem found, not just the first.</summary>
public sealed class DiscountValidationException : Exception
{
    public DiscountValidationException(IReadOnlyList<string> errors)
        : base("Invalid discount request: " + string.Join("; ", errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

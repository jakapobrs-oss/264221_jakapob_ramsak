using DiscountModule.Campaigns;

namespace DiscountModule;

/// <summary>One step of the discount breakdown.</summary>
/// <param name="Campaign">The campaign that was applied.</param>
/// <param name="PriceBefore">Order total before this campaign.</param>
/// <param name="DiscountAmount">Amount actually subtracted (already capped and rounded).</param>
/// <param name="PriceAfter">Order total after this campaign.</param>
/// <param name="Description">Short human readable explanation, e.g. "15% off Clothing (1,050.00 THB)".</param>
public sealed record AppliedDiscount(
    Campaign Campaign,
    decimal PriceBefore,
    decimal DiscountAmount,
    decimal PriceAfter,
    string Description);

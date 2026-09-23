namespace DiscountModule.Campaigns;

/// <summary>
/// Base type for every discount campaign. A campaign carries its own parameters, knows how to
/// validate them and how to compute its discount for a given order state. Adding a new campaign
/// means adding one derived record; the calculator does not need to change.
/// </summary>
public abstract record Campaign
{
    /// <summary>Category used for the one-per-category rule and for ordering.</summary>
    public abstract CampaignCategory Category { get; }

    /// <summary>Human readable campaign name used in the result breakdown.</summary>
    public abstract string Name { get; }

    /// <summary>Validates the campaign parameters. Returns one message per problem; empty when valid.</summary>
    public abstract IEnumerable<string> Validate();

    /// <summary>
    /// Computes the discount amount (THB, zero or more) for the current order state.
    /// Callers cap the returned value at <see cref="DiscountContext.RunningTotal"/>, so an
    /// implementation only needs to express its own rule.
    /// </summary>
    public abstract decimal ComputeDiscount(DiscountContext context);
}

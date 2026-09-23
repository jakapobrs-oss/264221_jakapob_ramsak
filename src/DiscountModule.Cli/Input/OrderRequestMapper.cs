using DiscountModule.Campaigns;
using DiscountModule.Models;

namespace DiscountModule.Cli.Input;

/// <summary>A parsed request ready to be handed to <see cref="DiscountCalculator"/>.</summary>
public sealed record OrderRequest(Cart Cart, IReadOnlyList<Campaign> Campaigns);

/// <summary>
/// Converts the loosely typed JSON shape into the module's domain types.
/// Structural problems (unknown campaign type, missing or unexpected parameters, unknown category)
/// are collected and reported together as an <see cref="InputFormatException"/>.
/// Value problems (negative price, percentage above 100, ...) are deliberately passed through:
/// the module validates business rules itself and reports them with its own messages.
/// </summary>
public static class OrderRequestMapper
{
    /// <summary>Which JSON parameters each campaign type accepts. Anything else is a mistake.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> ParametersByType =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["FixedAmount"] = ["amount"],
            ["Percentage"] = ["percentage"],
            ["CategoryPercentage"] = ["category", "percentage"],
            ["Points"] = ["points"],
            ["Seasonal"] = ["everyAmount", "discountAmount"],
        };

    public static string SupportedTypes => string.Join(", ", ParametersByType.Keys);

    public static string SupportedCategories => string.Join(", ", Enum.GetNames<ItemCategory>());

    /// <exception cref="InputFormatException">The request has structural problems (see class summary).</exception>
    public static OrderRequest Map(OrderRequestDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var errors = new List<string>();

        var items = MapItems(dto.Items, errors);
        var campaigns = MapCampaigns(dto.Campaigns, errors);

        if (errors.Count > 0)
        {
            throw new InputFormatException(errors);
        }

        return new OrderRequest(new Cart(items), campaigns);
    }

    private static List<CartItem> MapItems(List<CartItemDto>? dtos, List<string> errors)
    {
        var items = new List<CartItem>();
        if (dtos is null)
        {
            errors.Add("\"items\" is required");
            return items;
        }

        for (var index = 0; index < dtos.Count; index++)
        {
            var label = $"Item #{index + 1}";
            var dto = dtos[index];
            if (dto is null)
            {
                errors.Add($"{label}: must be an object, not null");
                continue;
            }

            var before = errors.Count;
            if (dto.Name is null)
            {
                errors.Add($"{label}: \"name\" is required");
            }

            if (dto.UnitPrice is null)
            {
                errors.Add($"{label}: \"unitPrice\" is required");
            }

            var category = ParseCategory(dto.Category, label, errors);

            if (errors.Count == before)
            {
                items.Add(new CartItem(dto.Name!, category!.Value, dto.UnitPrice!.Value, dto.Quantity ?? 1));
            }
        }

        return items;
    }

    private static List<Campaign> MapCampaigns(List<CampaignDto>? dtos, List<string> errors)
    {
        var campaigns = new List<Campaign>();
        if (dtos is null)
        {
            // No "campaigns" property at all is treated like an empty list: nothing to apply.
            return campaigns;
        }

        for (var index = 0; index < dtos.Count; index++)
        {
            var label = $"Campaign #{index + 1}";
            var dto = dtos[index];
            if (dto is null)
            {
                errors.Add($"{label}: must be an object, not null");
                continue;
            }

            if (dto.Type is null)
            {
                errors.Add($"{label}: \"type\" is required (one of: {SupportedTypes})");
                continue;
            }

            if (!ParametersByType.TryGetValue(dto.Type, out var allowed))
            {
                errors.Add($"{label}: unknown type '{dto.Type}' (supported: {SupportedTypes})");
                continue;
            }

            var campaign = MapCampaign(dto, allowed, label, errors);
            if (campaign is not null)
            {
                campaigns.Add(campaign);
            }
        }

        return campaigns;
    }

    private static Campaign? MapCampaign(CampaignDto dto, string[] allowed, string label, List<string> errors)
    {
        var before = errors.Count;
        var present = PresentParameters(dto);

        foreach (var name in allowed.Where(name => !present.Contains(name)))
        {
            errors.Add($"{label} ({dto.Type}): \"{name}\" is required");
        }

        foreach (var name in present.Where(name => !allowed.Contains(name)))
        {
            errors.Add($"{label} ({dto.Type}): unexpected parameter \"{name}\" (this type only takes: {string.Join(", ", allowed)})");
        }

        var category = allowed.Contains("category") ? ParseCategory(dto.Category, $"{label} ({dto.Type})", errors) : null;

        if (errors.Count != before)
        {
            return null;
        }

        return allowed[0] switch
        {
            "amount" => new FixedAmountCoupon(dto.Amount!.Value),
            "percentage" => new PercentageCoupon(dto.Percentage!.Value),
            "category" => new CategoryPercentageOnTop(category!.Value, dto.Percentage!.Value),
            "points" => new PointsOnTop(dto.Points!.Value),
            "everyAmount" => new SeasonalCampaign(dto.EveryAmount!.Value, dto.DiscountAmount!.Value),
            _ => throw new InvalidOperationException($"No mapping for campaign type '{dto.Type}'"),
        };
    }

    /// <summary>Names of the parameters the user actually supplied, using the JSON spelling.</summary>
    private static HashSet<string> PresentParameters(CampaignDto dto)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        if (dto.Amount is not null) present.Add("amount");
        if (dto.Percentage is not null) present.Add("percentage");
        if (dto.Category is not null) present.Add("category");
        if (dto.Points is not null) present.Add("points");
        if (dto.EveryAmount is not null) present.Add("everyAmount");
        if (dto.DiscountAmount is not null) present.Add("discountAmount");
        return present;
    }

    private static ItemCategory? ParseCategory(string? value, string label, List<string> errors)
    {
        if (value is null)
        {
            errors.Add($"{label}: \"category\" is required (one of: {SupportedCategories})");
            return null;
        }

        // TryParse also accepts numeric strings such as "7", so the IsDefined check is needed.
        if (Enum.TryParse<ItemCategory>(value, ignoreCase: true, out var category) && Enum.IsDefined(category))
        {
            return category;
        }

        errors.Add($"{label}: unknown category '{value}' (supported: {SupportedCategories})");
        return null;
    }
}

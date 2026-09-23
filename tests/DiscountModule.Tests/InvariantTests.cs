using DiscountModule.Campaigns;
using DiscountModule.Models;
using DiscountModule.Validation;

namespace DiscountModule.Tests;

/// <summary>
/// Rules that must hold for every valid order, checked on many random orders. The generator uses a fixed
/// seed, so every run checks the same orders, and a failure names the case that broke the rule.
/// </summary>
public class InvariantTests
{
    private const int Seed = 20260923;
    private const int OrderCount = 500;

    [Fact]
    public void The_final_price_is_never_negative_and_never_above_the_subtotal()
    {
        foreach (RandomOrder order in RandomOrders())
        {
            DiscountResult result = order.Calculate();

            Assert.True(result.FinalPrice >= 0 && result.FinalPrice <= result.Subtotal,
                $"{order}: final price {result.FinalPrice} is outside 0..{result.Subtotal}");
        }
    }

    [Fact]
    public void Every_step_starts_where_the_previous_one_ended()
    {
        foreach (RandomOrder order in RandomOrders())
        {
            DiscountResult result = order.Calculate();

            decimal expectedPriceBefore = result.Subtotal;
            foreach (AppliedDiscount step in result.AppliedDiscounts)
            {
                bool continues = step.PriceBefore == expectedPriceBefore
                    && step.DiscountAmount >= 0
                    && step.PriceAfter == step.PriceBefore - step.DiscountAmount;
                Assert.True(continues, $"{order}: {step} does not continue from {expectedPriceBefore}");
                expectedPriceBefore = step.PriceAfter;
            }

            Assert.True(result.FinalPrice == expectedPriceBefore,
                $"{order}: final price {result.FinalPrice} differs from the last step ({expectedPriceBefore})");
        }
    }

    [Fact]
    public void Passing_the_campaigns_in_reverse_order_gives_the_same_result()
    {
        foreach (RandomOrder order in RandomOrders())
        {
            DiscountResult asGiven = order.Calculate();
            DiscountResult reversed = DiscountCalculator.Calculate(order.Cart, [.. Enumerable.Reverse(order.Campaigns)]);

            Assert.True(asGiven.AppliedDiscounts.SequenceEqual(reversed.AppliedDiscounts) && asGiven.FinalPrice == reversed.FinalPrice,
                $"{order}: reversing the campaigns changed the result");
        }
    }

    [Fact]
    public void Every_amount_in_the_result_has_exactly_two_decimal_places()
    {
        foreach (RandomOrder order in RandomOrders())
        {
            DiscountResult result = order.Calculate();

            decimal[] amounts =
            [
                result.Subtotal,
                result.FinalPrice,
                result.TotalDiscount,
                .. result.AppliedDiscounts.SelectMany(step => new[] { step.PriceBefore, step.DiscountAmount, step.PriceAfter }),
            ];
            foreach (decimal amount in amounts)
            {
                Assert.True(amount.Scale == 2, $"{order}: {amount} does not have exactly 2 decimal places");
            }
        }
    }

    [Fact]
    public void Points_used_are_the_points_given_capped_at_20_percent_of_the_total_at_that_step()
    {
        foreach (RandomOrder order in RandomOrders())
        {
            DiscountResult result = order.Calculate();
            AppliedDiscount? pointsStep = result.AppliedDiscounts.SingleOrDefault(step => step.Campaign is PointsOnTop);
            if (pointsStep is null)
            {
                Assert.True(result.PointsUsed == 0, $"{order}: {result.PointsUsed} points used without a points campaign");
                continue;
            }

            int given = ((PointsOnTop)pointsStep.Campaign).Points;
            decimal cap = Math.Floor(pointsStep.PriceBefore * PointsOnTop.MaxShareOfTotal);
            Assert.True(result.PointsUsed == Math.Min(given, cap),
                $"{order}: used {result.PointsUsed} points; given {given}, cap {cap} at {pointsStep.PriceBefore}");
        }
    }

    /// <summary>One random valid order, labelled with its case number so a failure can be traced back.</summary>
    private sealed record RandomOrder(int Case, Cart Cart, Campaign[] Campaigns)
    {
        public DiscountResult Calculate()
        {
            try
            {
                return DiscountCalculator.Calculate(Cart, Campaigns);
            }
            catch (DiscountValidationException exception)
            {
                // Every generated order should be valid, so this is a bug in the generator below.
                throw new InvalidOperationException($"{this} was generated invalid: {exception.Message}", exception);
            }
        }

        public override string ToString()
            => $"seed {Seed}, case {Case}: items [{string.Join(", ", Cart.Items)}], campaigns [{string.Join(", ", (IEnumerable<Campaign>)Campaigns)}]";
    }

    /// <summary>
    /// The same <see cref="OrderCount"/> orders on every run. Each has 1-5 lines with whole-satang prices
    /// (free items included) and at most one campaign per category, with parameters inside their valid ranges.
    /// </summary>
    private static IEnumerable<RandomOrder> RandomOrders()
    {
        Random random = new(Seed);
        for (int orderCase = 0; orderCase < OrderCount; orderCase++)
        {
            yield return new RandomOrder(orderCase, RandomCart(random), RandomCampaigns(random));
        }
    }

    private static Cart RandomCart(Random random)
    {
        CartItem[] items = new CartItem[random.Next(1, 6)];
        for (int index = 0; index < items.Length; index++)
        {
            items[index] = new CartItem($"Item {index + 1}", RandomCategory(random), Amount(random, 0m, 5_000m), random.Next(1, 4));
        }

        return new Cart(items);
    }

    private static Campaign[] RandomCampaigns(Random random)
    {
        List<Campaign> campaigns = [];

        // Each category is left out one time in three, so orders with 0 to 3 campaigns all occur.
        if (random.Next(3) > 0)
        {
            campaigns.Add(random.Next(2) == 0
                ? new FixedAmountCoupon(Amount(random, 0.01m, 3_000m))
                : new PercentageCoupon(Amount(random, 0.01m, 100m)));
        }

        if (random.Next(3) > 0)
        {
            campaigns.Add(random.Next(2) == 0
                ? new CategoryPercentageOnTop(RandomCategory(random), Amount(random, 0.01m, 100m))
                : new PointsOnTop(random.Next(1, 3_000)));
        }

        if (random.Next(3) > 0)
        {
            decimal everyAmount = Amount(random, 0.01m, 1_000m);
            campaigns.Add(new SeasonalCampaign(everyAmount, Amount(random, 0.01m, everyAmount)));
        }

        // Shuffled, so the calculator also receives the campaigns in a random order.
        return [.. campaigns.OrderBy(_ => random.Next())];
    }

    /// <summary>A random amount with 2 decimal places from <paramref name="min"/> to <paramref name="max"/>.</summary>
    private static decimal Amount(Random random, decimal min, decimal max)
        => random.NextInt64((long)(min * 100), (long)(max * 100) + 1) / 100m;

    private static ItemCategory RandomCategory(Random random)
    {
        ItemCategory[] categories = Enum.GetValues<ItemCategory>();
        return categories[random.Next(categories.Length)];
    }
}

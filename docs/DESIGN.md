# Design notes — Discount module

This document is the binding specification for the implementation. The public types in
`src/DiscountModule` are the contract; everything below explains how they must behave.

## 1. Scope

The module takes (a) the items in a shopping cart and (b) a list of discount campaigns with their
parameters, and returns the final price plus a step-by-step breakdown. Cart management, inventory
and checkout are out of scope. The command line program in `src/DiscountModule.Cli` is only an input
adapter: it reads a JSON file, calls the module and prints the result.

## 2. Domain model

| Type | Role |
|------|------|
| `Cart`, `CartItem`, `ItemCategory` | Input cart. `CartItem.LineTotal = UnitPrice * Quantity`. `Cart.Subtotal` = sum of line totals. |
| `Campaign` (abstract record) | One campaign with its parameters. Knows its `Category`, validates its own parameters (`Validate()`) and computes its discount (`ComputeDiscount(DiscountContext)`). |
| `FixedAmountCoupon`, `PercentageCoupon` | Category **Coupon** |
| `CategoryPercentageOnTop`, `PointsOnTop` | Category **On Top** |
| `SeasonalCampaign` | Category **Seasonal** |
| `DiscountContext` | `Cart` + `RunningTotal` (total after the campaigns applied so far). |
| `DiscountCalculator.Calculate(cart, campaigns)` | Entry point. Validates, orders, applies. |
| `DiscountResult` | `Subtotal`, `AppliedDiscounts` (one `AppliedDiscount` per campaign, in application order), `FinalPrice`, `TotalDiscount`, `PointsUsed`. |
| `DiscountValidationException` | Thrown by `Calculate` with **all** validation errors collected. |

Adding a campaign type = adding one derived record. The calculator is closed for modification.

## 3. Calculation algorithm (`DiscountCalculator.Calculate`)

1. Validate the cart and the campaign list (section 5). If any error → throw `DiscountValidationException` with every error found.
2. `running = cart.Subtotal` (exact: monetary inputs are validated to at most 2 decimals, see A8).
3. Sort campaigns by `CampaignCategory` (Coupon = 1, On Top = 2, Seasonal = 3). The order in which the caller passed them is irrelevant.
4. For each campaign in that order:
   - `raw = campaign.ComputeDiscount(new DiscountContext(cart, running))`
   - `discount = Money.Round(Math.Min(raw, running))` — never more than what is left, never negative.
   - record `AppliedDiscount(campaign, priceBefore: running, discount, priceAfter: running - discount, description)`
   - `running -= discount`
5. Return `DiscountResult(subtotal, applied, finalPrice: running)`.

Invariants: `FinalPrice >= 0`, `FinalPrice <= Subtotal`, `TotalDiscount = Subtotal - FinalPrice`,
each step `PriceAfter = PriceBefore - DiscountAmount`, the input cart is never mutated.

## 4. Campaign rules

| Campaign | Discount (before cap at running total) |
|----------|-----------------------------------------|
| Fixed amount (`Amount`) | `Amount` |
| Percentage (`Percentage`) | `running * Percentage / 100` |
| Percentage by item category (`ItemCategory`, `Percentage`) | `sum(LineTotal of items in ItemCategory) * Percentage / 100` — computed on the **original** item prices (see A3) |
| Points (`Points`) | `min(Points, floor(running * 0.20))` — whole THB only (see A2) |
| Seasonal (`EveryAmount`, `DiscountAmount`) | `floor(running / EveryAmount) * DiscountAmount` |

`Money.Round(x)` = round to 2 decimal places, `MidpointRounding.AwayFromZero` (satang precision).

### Worked examples from the assignment (all must pass as tests)

| # | Cart | Campaign | Final |
|---|------|----------|-------|
| 1 | T-Shirt 350, Hat 250 | Fixed amount 50 | 550 |
| 2 | T-Shirt 350, Hat 250 | Percentage 10% | 540 |
| 3 | T-Shirt 350 (Clothing), Hoodie 700 (Clothing), Watch 850 (Electronics), Bag 640 (Accessories) | 15% off Clothing | 2,382.50 |
| 4 | T-Shirt 350, Hat 250, Belt 230 | 68 points | 762 |
| 5 | T-Shirt 350, Hat 250, Belt 230 | 40 THB at every 300 THB | 750 |

Combined example (own): cart 830 (T-Shirt 350, Hat 250, Belt 230), Percentage 10% → 747.00,
Points 100 (cap floor(149.40) = 149 → uses 100) → 647.00, Seasonal 40 at every 300
(floor(647/300) = 2 → 80) → **567.00**. Passing the same campaigns in any other order gives the same result.

## 5. Validation rules (all errors are collected, then thrown together)

Cart
- cart must not be null; `Items` must not be null or empty; no null entries
- each item: `Name` not blank; `0 <= UnitPrice <= 1,000,000,000` with at most 2 decimals;
  `1 <= Quantity <= 1,000,000`; `Category` must be a defined `ItemCategory`

Campaign list
- list must not be null; may be empty (final price = subtotal); no null entries
- every campaign's `Category` must be a defined `CampaignCategory` (guards against a badly written derived campaign)
- **at most one campaign per category** — `FixedAmountCoupon` + `PercentageCoupon` is rejected,
  and so is `CategoryPercentageOnTop` + `PointsOnTop` (both are On Top)
- `FixedAmountCoupon.Amount` in (0, 1,000,000,000], at most 2 decimals
- `PercentageCoupon.Percentage` in (0, 100]
- `CategoryPercentageOnTop.Percentage` in (0, 100]; `ItemCategory` defined
- `PointsOnTop.Points >= 1`
- `SeasonalCampaign.EveryAmount` and `DiscountAmount` in (0, 1,000,000,000], at most 2 decimals; `DiscountAmount <= EveryAmount`

Bounds are checked before any arithmetic, so absurd input is reported, never an `OverflowException`.

Error messages name the offending campaign/item and the rule, e.g.
`"Only one Coupon campaign can be applied per order (got: Fixed amount, Percentage discount)"`,
`"Item #2 'Hat': quantity must be at least 1 (got 0)"`.

## 6. Assumptions (stated because the assignment leaves them open)

- **A1 — "Total price" means the running total.** Each campaign works on the price left after the
  campaigns before it (Coupon → On Top → Seasonal). The 20% points cap and the seasonal "every X THB"
  therefore use the running total, not the original subtotal.
- **A2 — Points are whole units.** 1 point = 1 THB, so the cap `20% × running total` is rounded
  **down** to a whole baht and the points actually used are always an integer (`DiscountResult.PointsUsed`).
  Example: running total 762.50 → cap 152; a customer spending 200 points uses 152.
- **A3 — Category percentage uses original item prices.** "Discount the entire amount of a specific
  category of items" is read as a percentage of those items' listed prices, regardless of a coupon
  applied earlier. The result is still capped at the running total.
- **A4 — "Amount" for the category campaign is a percentage.** The parameter table says "Amount" but
  the example says "15% Off on Clothing"; the example wins.
- **A5 — Seasonal uses floor division** (830 THB with "40 at every 300" → 2 × 40 = 80, matching the
  example). `DiscountAmount` may not exceed `EveryAmount`; otherwise the campaign would discount more
  than 100% per step.
- **A6 — The final price never goes below zero.** A 1,000 THB coupon on a 600 THB cart gives 0.
- **A7 — One campaign per category is enforced, not resolved.** Passing two campaigns of the same
  category is a usage error and is rejected with a clear message rather than silently picking one.
- **A8 — Money is `decimal` with 2-decimal (satang) precision.** Monetary inputs (prices, fixed
  amounts, seasonal thresholds) must already be whole satang, so the subtotal is exact; computed
  discounts are rounded half away from zero to 2 decimals. Prices such as 2,382.50 stay exact.
- **A9 — Items carry a quantity.** "1 T-Shirt: 350 THB" is quantity 1 × unit price 350.
- **A10 — A free item (unit price 0) is allowed; an empty cart is not.** Applying discounts to an
  empty order is treated as a mistake.
- **A11 — Supported magnitudes.** Unit prices and campaign amounts up to 1,000,000,000 THB and
  quantities up to 1,000,000 per line are supported; larger values are rejected with a message.
  Within these bounds no intermediate calculation can overflow `decimal`.

## 7. JSON input (CLI)

```json
{
  "items": [
    { "name": "T-Shirt", "category": "Clothing", "unitPrice": 350, "quantity": 1 },
    { "name": "Hat", "category": "Accessories", "unitPrice": 250 }
  ],
  "campaigns": [
    { "type": "FixedAmount", "amount": 50 },
    { "type": "Percentage", "percentage": 10 },
    { "type": "CategoryPercentage", "category": "Clothing", "percentage": 15 },
    { "type": "Points", "points": 68 },
    { "type": "Seasonal", "everyAmount": 300, "discountAmount": 40 }
  ]
}
```

The document above is a catalogue of every campaign shape. As a real request it would be rejected
(two Coupon and two On Top campaigns); a valid request has at most one campaign per category, see
`examples/06-all-categories.json`.

`quantity` defaults to 1. `type` and `category` are case-insensitive. Unknown `type`, unknown
`category`, missing or non-numeric fields, malformed JSON and a missing file are reported as errors
(exit code 2); validation errors from the module are printed one per line (exit code 1); success = 0.

## 8. Test matrix

Core (`tests/DiscountModule.Tests`)
- the 5 worked examples + the combined example
- order independence (campaigns passed shuffled)
- one-per-category: Coupon pair, On Top pair, and a valid one-of-each set
- caps: fixed amount larger than cart → 0; points above cap → cap; points cap rounds down (762.50 → 152); seasonal never exceeds running total
- category campaign with no matching items → 0 discount, still listed in the breakdown
- quantity > 1 and free items
- rounding: 10% of 333.33 → discount 33.33, final 300.00; cart 0.05 with a 10% coupon → discount 0.01,
  final 0.04 (half away from zero, not banker's rounding)
- coupon before points cap: 830, 10% coupon, 200 points, seasonal 40 at every 300 → 558.00 with
  149 points used (proves A1 and A2)
- coupon before category percentage: Clothing 100 + Accessories 100, 10% coupon, 10% off Clothing
  → 170.00 (proves A3)
- empty campaign list → final = subtotal, empty breakdown
- every validation rule in section 5, plus "all errors collected at once"; out-of-range and
  three-decimal amounts are rejected with a message, never an exception of another type
- a derived campaign returning an undefined `CampaignCategory` is rejected
- `DiscountValidationException.Errors` is a snapshot (mutating the caller's list afterwards changes nothing)
- input cart not mutated

CLI
- each example file produces the expected final price
- unknown campaign type / unknown category / malformed JSON / missing file → exit code 2 with message
- invalid values (e.g. negative price) → exit code 1 with the module's messages

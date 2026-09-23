# Discount Module

A small, well-tested .NET library that calculates the final price of an order by applying discount
campaigns to the items in a shopping cart, plus a command line front end that reads an order from a
JSON file. Built for the Playtorium developer take-home assignment.

- **Language / runtime:** C# 14 on .NET 10. `decimal` for money, records, no third-party
  packages in the module or the CLI (the tests use xUnit).
- **Tests:** `dotnet test` runs both test projects (see [Tests](#tests)).
- **Design notes and assumptions:** [`docs/DESIGN.md`](docs/DESIGN.md), summarised below.

## What it does

| Campaign | Category | Rule | Parameters |
|----------|----------|------|------------|
| Fixed amount | Coupon | Subtract a fixed amount from the total | `amount` |
| Percentage discount | Coupon | Subtract a percentage of the total | `percentage` |
| Percentage discount by item category | On Top | Subtract a percentage of the price of every item in one category | `category`, `percentage` |
| Discount by points | On Top | 1 point = 1 THB, capped at 20% of the total (fixed rule) | `points` |
| Special campaign | Seasonal | For every X THB of the total, subtract Y THB | `everyAmount`, `discountAmount` |

Rules from the assignment:

1. At most **one campaign per category** (Coupon, On Top, Seasonal). Two coupons, or a category
   discount together with points, is rejected as a usage error.
2. Campaigns are applied in the order **Coupon → On Top → Seasonal**, whatever order they are given in.
   Each discount is capped at the price left by the campaigns before it. Points and the seasonal
   campaign are also calculated from that remaining price; the category discount is a percentage of
   the listed prices of that category (assumption A3).

The module returns the final price together with a step-by-step breakdown (price before, discount,
price after for every campaign) and the number of points actually used, so a caller can deduct them.

![How an order is priced](docs/diagrams/pricing-pipeline.svg)

## Quick start

Requirements: [.NET SDK 10.0](https://dotnet.microsoft.com/download/dotnet/10.0), version 10.0.100 or later.

```bash
dotnet test                                                             # run all tests
dotnet run --project src/DiscountModule.Cli -- examples/06-all-categories.json
```

```
Items
  1 x T-Shirt (Clothing) @ 350.00                                            350.00
  1 x Hat (Accessories) @ 250.00                                             250.00
  1 x Belt (Accessories) @ 230.00                                            230.00
Subtotal                                                                     830.00

Discounts (applied in order: Coupon > On Top > Seasonal)
  [Coupon] 10% off 830.00 THB                                                -83.00
  [On Top] 100 points (cap 149 = 20% of 747.00 THB)                         -100.00
  [Seasonal] 40.00 THB off every 300.00 THB of 647.00 THB (2 x 40.00)        -80.00

Total discount                                                              -263.00
Final price                                                                  567.00
Points used: 100
```

No .NET SDK installed? The tests also run in the official SDK container:

```bash
docker run --rm -v "$PWD:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test
```

Other ways to call the CLI:

```bash
dotnet run --project src/DiscountModule.Cli -- examples/04-points.json --json    # machine readable output
cat examples/01-fixed-amount.json | dotnet run --project src/DiscountModule.Cli -- -   # read from stdin
dotnet run --project src/DiscountModule.Cli -- examples/invalid-values.json      # see how errors are reported
dotnet run --project src/DiscountModule.Cli -- --help
```

Exit codes: `0` success · `1` the order breaks a discount rule · `2` the input could not be read or
understood (missing file, malformed JSON, unknown campaign type, bad usage) · `3` unexpected error.
Problems are printed on stderr, one per line.

## Input format

```json
{
  "items": [
    { "name": "T-Shirt", "category": "Clothing", "unitPrice": 350, "quantity": 1 },
    { "name": "Hat", "category": "Accessories", "unitPrice": 250 }
  ],
  "campaigns": [
    { "type": "Percentage", "percentage": 10 },
    { "type": "Points", "points": 68 },
    { "type": "Seasonal", "everyAmount": 300, "discountAmount": 40 }
  ]
}
```

- `category` is one of `Clothing`, `Accessories`, `Electronics`. `quantity` is a whole number and
  defaults to 1 when left out.
- `type` is one of `FixedAmount` (`amount`), `Percentage` (`percentage`), `CategoryPercentage`
  (`category`, `percentage`), `Points` (`points`), `Seasonal` (`everyAmount`, `discountAmount`).
- Property names, types and categories match in any letter case. Comments and trailing commas are allowed.
- `campaigns` may be empty or left out: the final price is then the subtotal.
- The file is checked strictly, and every problem is reported with its JSON path: a missing or
  unexpected property (even one set to `null`), a property given twice, a number written as a string,
  a fractional quantity, a number a `decimal` cannot hold exactly (such as `1e-30`), an unknown type or
  category. `null` is never a valid value; leave an optional property out instead. When a campaign's
  `type` is missing or unknown, its other properties are not checked, because which ones belong
  depends on the type.

All files in [`examples/`](examples/) are runnable:

| File | Shows |
|------|-------|
| `01`–`05` | the five worked examples from the assignment |
| `06-all-categories.json` | one campaign of each category, listed in reverse order on purpose (the result is the same) |
| `07-points-capped-after-coupon.json` | the 20% points cap taken from the price after the coupon (558.00, 149 points used) |
| `08-coupon-then-category.json` | the category discount taken from listed prices after a coupon (170.00) |
| `invalid-two-coupons.json` | the one-campaign-per-category rule (exit code 1) |
| `invalid-values.json` | six broken business rules reported together (exit code 1) |
| `invalid-format.json` | five problems with the file's shape reported together (exit code 2) |

## Using the module from code

```csharp
using DiscountModule;
using DiscountModule.Campaigns;
using DiscountModule.Models;

var cart = new Cart(
[
    new CartItem("T-Shirt", ItemCategory.Clothing, 350m),
    new CartItem("Hat", ItemCategory.Accessories, 250m),
    new CartItem("Belt", ItemCategory.Accessories, 230m),
]);

Campaign[] campaigns =
[
    new PercentageCoupon(10m),
    new PointsOnTop(100),
    new SeasonalCampaign(EveryAmount: 300m, DiscountAmount: 40m),
];

DiscountResult result = DiscountCalculator.Calculate(cart, campaigns);
// result.Subtotal == 830.00m, result.FinalPrice == 567.00m, result.PointsUsed == 100
// result.AppliedDiscounts: one entry per campaign, in application order

// Invalid input never yields a price: every problem is collected and thrown at once.
try
{
    DiscountCalculator.Calculate(cart, [new FixedAmountCoupon(50m), new PercentageCoupon(10m)]);
}
catch (DiscountModule.Validation.DiscountValidationException ex)
{
    // ex.Errors: ["Only one Coupon campaign can be applied per order (got: Fixed amount, Percentage discount)"]
}
```

## Assumptions

The assignment leaves a few points open. These are the readings used, with the reasoning in
[`docs/DESIGN.md` §6](docs/DESIGN.md):

| # | Assumption |
|---|------------|
| A1 | **"Total price" means the running total.** The 20% points cap and the seasonal "every X THB" use the price left by the campaigns before them, not the original subtotal. |
| A2 | **Points are whole units.** 1 point = 1 THB, so the cap is rounded *down* to a whole baht and `PointsUsed` is always an integer (running total 762.50 → cap 152). |
| A3 | **The category percentage uses the listed item prices**, even after a coupon; the result is still capped at the running total. |
| A4 | **The category campaign's "Amount" is a percentage.** The parameter table says "Amount", the example says "15% Off on Clothing"; the example wins. |
| A5 | **Seasonal uses floor division** (830 THB at "40 for every 300" → 2 × 40), matching the example. `discountAmount` may not exceed `everyAmount`. |
| A6 | **The final price never goes below zero.** A 1,000 THB coupon on a 600 THB cart gives 0. |
| A7 | **One campaign per category is enforced, not resolved.** Two campaigns of the same category are rejected with a clear message rather than silently choosing one. |
| A8 | **Money is `decimal` at satang precision.** Prices and amounts may have at most 2 decimals, and so may percentages (12.5 or 33.33, not 33.333). Together with the limits in A11 this keeps every calculation exact, so each discount is rounded only once: half away from zero, to 2 decimals. |
| A9 | **Items carry a quantity** (`unitPrice × quantity`). |
| A10 | **A free item (price 0) is allowed; an empty cart is not.** |
| A11 | **Supported magnitudes:** prices and amounts up to 1,000,000,000 THB, quantities up to 1,000,000 per line, and an order subtotal up to 1,000,000,000,000,000 THB (one line at the maximum price and quantity). Larger values are rejected; within these bounds no calculation can overflow or lose a digit. |
| A12 | **Item categories in the examples.** The assignment names the items but not their categories. T-Shirt and Hoodie must be Clothing for the 2,382.50 example to work; Hat, Belt and Bag are taken as Accessories and Watch as Electronics. None of these choices changes any example's result. |

## Validation and resilience

`DiscountCalculator.Calculate` validates everything before it calculates anything, and reports **all**
problems in one `DiscountValidationException` (`Errors` has one message per problem):

- cart: missing or empty item list, missing items, blank name, negative or out-of-range price, a price
  with more than 2 decimals, quantity below 1 or above the limit, undefined category, a subtotal above
  the limit
- campaigns: missing entries, more than one campaign per category, amount ≤ 0, out of range or with more
  than 2 decimals, percentage outside (0, 100] or with more than 2 decimals, points below 1, seasonal
  threshold or discount ≤ 0, discount above the threshold, a campaign type reporting an undefined category

The calculator works on its own copy of the item and campaign lists, so a caller changing them from
another thread cannot slip an unchecked value between validation and calculation. Messages and
descriptions format numbers the same way on every machine, whatever its culture settings.

The CLI checks the file in two passes. It first reports the problems with the file's shape (exit
code 2). Once the shape is right, the module reports every broken business rule (exit code 1). A JSON
syntax error stops the first pass at that error, since nothing after it can be read reliably. Text
taken from the input, such as an item name, is printed with control characters escaped (`\n`, `\t`),
so each item and each problem stays on its own line.

## Project structure

```
src/DiscountModule/              the module (class library, no dependencies)
  Models/                        Cart, CartItem, ItemCategory
  Campaigns/                     Campaign base record, one record per campaign, CampaignCategory (+ display names), DiscountContext
  Validation/                    CartValidator, CampaignListValidator, AmountRules, DiscountValidationException
  DiscountCalculator.cs          entry point: validate → order → apply → breakdown
  DiscountResult.cs              the result; AppliedDiscount.cs is one step of the breakdown
  Money.cs, InputLimits.cs       satang rounding and formatting; supported magnitudes
src/DiscountModule.Cli/          command line front end (JSON in, text or JSON out)
  Input/                         OrderRequestParser (JSON → cart + campaigns), JsonObjectReader, CampaignTypes
  Output/                        text and JSON printers
tests/DiscountModule.Tests/      unit tests of the module (worked examples, rules, order, validation, breakdown, culture)
tests/DiscountModule.Cli.Tests/  tests of the CLI (parsing, exit codes, output, example files)
examples/                        runnable order files
docs/DESIGN.md                   binding design notes, assumptions, test matrix
docs/diagrams/                   the two diagrams used in this README
```

![Type model](docs/diagrams/type-model.svg)

## Design notes

- **Each campaign owns its rule.** `Campaign` is an abstract record; every campaign type validates its
  own parameters, computes its own discount from a `DiscountContext` (cart + running total) and describes
  what it did for the breakdown. A new campaign is a new record, and `DiscountCalculator` does not change
  (open/closed). Accepting it from JSON takes one more line in the CLI's `CampaignTypes` table.
- **The calculator owns the cross-cutting rules**: one campaign per category, application order,
  capping at the running total, rounding, and the breakdown. Those rules belong to the order, not to
  any single campaign.
- **Validate first, then calculate.** Invalid input never produces a price. Errors are collected so a
  caller (or a UI) can show everything that is wrong at once.
- **`decimal` everywhere.** In binary floating point `0.1 + 0.2` is `0.30000000000000004`; `decimal`
  stores baht and satang exactly. Rounding to 2 decimals happens once per applied discount.
- **Records with init-only properties** for input and output. The calculator never changes what it is
  given and returns a read-only breakdown.
- **The CLI is a thin adapter.** It turns JSON into domain types and formats the result. It contains no
  discount logic, so the module can sit behind a web API or a UI unchanged.

## Tests

```bash
dotnet test                       # both projects
dotnet test tests/DiscountModule.Tests
dotnet test tests/DiscountModule.Cli.Tests
```

Coverage highlights: the five worked examples from the assignment and three combined orders; order
independence; one-per-category enforcement; caps (final price ≥ 0, points cap including rounding down,
seasonal never exceeding the total); rounding (including the 0.005 midpoint); every validation rule,
including out-of-range magnitudes and 3-decimal amounts; culture-independent messages; every shape
problem the CLI reports, with its exact message; each CLI exit code (0 to 3); Thai item names and control
characters in the text output.

`.github/workflows/ci.yml` builds and runs the same tests with GitHub Actions.

## Limitations

- The text output aligns columns for Latin and Thai text; wide East Asian characters are not measured.
- The CLI reads the whole order into memory, which suits a shopping cart but not a bulk import.
- Amounts are THB only; there is no currency handling.

# Discount Module

A small, well-tested .NET library that calculates the final price of an order by applying discount
campaigns to the items in a shopping cart, plus a command line front end that reads an order from a
JSON file. Built for the Playtorium developer take-home assignment.

- **Language / runtime:** C# 14 on .NET 10 (`decimal` money, records, no third-party packages)
- **Tests:** xUnit — `dotnet test` runs everything (see [Tests](#tests))
- **Design notes and assumptions:** [`docs/DESIGN.md`](docs/DESIGN.md) (summarised below)

## What it does

| Campaign | Category | Rule | Parameters |
|----------|----------|------|------------|
| Fixed amount | Coupon | Subtract a fixed amount from the total | `amount` |
| Percentage discount | Coupon | Subtract a percentage of the total | `percentage` |
| Percentage discount by item category | On Top | Subtract a percentage of the price of every item in one category | `category`, `percentage` |
| Discount by points | On Top | 1 point = 1 THB, capped at 20 % of the total (fixed rule) | `points` |
| Special campaign | Seasonal | For every X THB of the total, subtract Y THB | `everyAmount`, `discountAmount` |

Rules from the assignment:

1. At most **one campaign per category** (Coupon, On Top, Seasonal). Two coupons, or a category
   discount together with points, is rejected as a usage error.
2. Campaigns are applied in the order **Coupon → On Top → Seasonal**, whatever order they are given in.
   Each campaign works on the price left by the previous one.

The module returns the final price together with a step-by-step breakdown (price before, discount,
price after for every campaign) and the number of points actually used, so a caller can deduct them.

![How an order is priced](docs/diagrams/pricing-pipeline.svg)

## Quick start

Requirements: [.NET SDK 10.0](https://dotnet.microsoft.com/download/dotnet/10.0) (any 10.0.x, see `global.json`).

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

Total discount                                                               263.00
Final price                                                                  567.00
Points used: 100
```

Other ways to call it:

```bash
dotnet run --project src/DiscountModule.Cli -- examples/04-points.json --json   # machine readable output
cat examples/01-fixed-amount.json | dotnet run --project src/DiscountModule.Cli -- -   # read from stdin
dotnet run --project src/DiscountModule.Cli -- --help
```

Exit codes: `0` success · `1` the order violates a discount rule (details on stderr) · `2` the input
could not be read (missing file, malformed JSON, unknown campaign type, bad usage).

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

- `category` is one of `Clothing`, `Accessories`, `Electronics`; `quantity` defaults to 1.
- `type` is one of `FixedAmount` (`amount`), `Percentage` (`percentage`), `CategoryPercentage`
  (`category`, `percentage`), `Points` (`points`), `Seasonal` (`everyAmount`, `discountAmount`).
  Names are case-insensitive. A parameter that does not belong to the type, a missing parameter,
  an unknown property (typo) or a number written as a string is reported with its location.
- `campaigns` may be empty or omitted: the final price is then the subtotal.

All files in [`examples/`](examples/) are runnable; `01`–`05` are the worked examples from the
assignment, `06`–`08` combine categories, `invalid-*` show the two kinds of error.

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

// Invalid input never yields a wrong price: every problem is collected and thrown at once.
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
| A1 | **"Total price" means the running total.** Each campaign works on the price left by the campaigns before it. The 20 % points cap and the seasonal "every X THB" use that running total. |
| A2 | **Points are whole units.** 1 point = 1 THB, so the cap is rounded *down* to a whole baht and `PointsUsed` is always an integer (running total 762.50 → cap 152). |
| A3 | **The category percentage uses the original item prices**, regardless of a coupon applied earlier; the result is still capped at the running total. |
| A4 | **The category campaign's "Amount" is a percentage.** The parameter table says "Amount", the example says "15 % Off on Clothing"; the example wins. |
| A5 | **Seasonal uses floor division** (830 THB at "40 for every 300" → 2 × 40), matching the example. `discountAmount` may not exceed `everyAmount`. |
| A6 | **The final price never goes below zero.** A 1,000 THB coupon on a 600 THB cart gives 0. |
| A7 | **One campaign per category is enforced, not resolved.** Two campaigns of the same category are rejected with a clear message rather than silently choosing one. |
| A8 | **Money is `decimal` at satang precision.** Monetary inputs must have at most 2 decimals; computed discounts are rounded half away from zero to 2 decimals. |
| A9 | **Items carry a quantity** (`unitPrice × quantity`). |
| A10 | **A free item (price 0) is allowed; an empty cart is not.** |
| A11 | **Supported magnitudes:** prices and amounts up to 1,000,000,000 THB, quantities up to 1,000,000 per line. Larger values are rejected; within the bounds nothing can overflow. |

## Validation and resilience

`DiscountCalculator.Calculate` validates everything before touching a single number and reports
**all** problems in one `DiscountValidationException` (`Errors` has one message per problem):

- cart: null/empty item list, null items, blank name, negative or out-of-range price, more than
  2 decimals, quantity below 1 or above the limit, undefined category
- campaigns: null entries, more than one campaign per category, amount ≤ 0 or out of range,
  percentage outside (0, 100], points below 1, seasonal threshold/discount ≤ 0 or discount > threshold,
  a campaign with an undefined category

The CLI adds a second layer for the file itself: malformed JSON (with line and JSON path), unknown
campaign types or categories (listing the valid ones), missing or unexpected parameters, typos in
property names. Both layers collect every problem instead of stopping at the first.

## Project structure

```
src/DiscountModule/              the module (class library, no dependencies)
  Models/                        Cart, CartItem, ItemCategory
  Campaigns/                     Campaign base record + one record per campaign, CampaignCategory, DiscountContext
  Validation/                    CartValidator, CampaignListValidator, AmountRules, DiscountValidationException
  DiscountCalculator.cs          entry point: validate → order → apply → breakdown
  DiscountResult.cs              result + AppliedDiscount steps
  Money.cs, InputLimits.cs       satang rounding/formatting; supported magnitudes
src/DiscountModule.Cli/          command line front end (JSON in, text/JSON out)
  Input/                         JSON shape (DTOs), reader, mapper to domain types
  Output/                        text and JSON printers
tests/DiscountModule.Tests/      unit tests of the module (worked examples, rules, order, validation, breakdown)
tests/DiscountModule.Cli.Tests/  tests of the CLI (parsing, mapping, exit codes, example files)
examples/                        runnable order files
docs/DESIGN.md                   binding design notes, assumptions, test matrix
docs/diagrams/                   the two diagrams used in this README
```

![Type model](docs/diagrams/type-model.svg)

## Design notes

- **Each campaign owns its rule.** `Campaign` is an abstract record; every campaign type validates
  its own parameters, computes its own discount from a `DiscountContext` (cart + running total) and
  describes what it did for the breakdown. Adding a campaign means adding one record;
  `DiscountCalculator` does not change (open/closed).
- **The calculator owns the cross-cutting rules**: one campaign per category, application order,
  capping at the running total, rounding, and the breakdown. Those rules belong to the order, not to
  any single campaign.
- **Validate first, then calculate.** Invalid input can never produce a price. Errors are collected
  so a caller (or a UI) can show everything that is wrong at once.
- **`decimal` everywhere, 2-decimal rounding at the point a discount is applied.** Binary floating
  point would turn 2,382.50 into 2,382.4999….
- **Immutable records** for input and output: the module cannot mutate the caller's cart, and results
  are safe to pass around.
- **The CLI is a thin adapter.** It converts JSON to domain types and formats the result; it contains
  no discount logic, so the module can be reused behind a web API or a UI unchanged.

## Tests

```bash
dotnet test                       # all projects
dotnet test tests/DiscountModule.Tests
dotnet test tests/DiscountModule.Cli.Tests
```

Coverage highlights: the five worked examples from the assignment and three combined orders; order
independence; one-per-category enforcement; caps (final price ≥ 0, points cap incl. rounding down,
seasonal never exceeding the total); rounding (incl. the 0.005 midpoint); every validation rule,
including out-of-range magnitudes and 3-decimal prices; CLI exit codes and error messages for each
kind of bad input. A GitHub Actions workflow (`.github/workflows/ci.yml`) runs the same `dotnet test`.

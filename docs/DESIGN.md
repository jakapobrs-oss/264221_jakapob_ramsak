# Design notes — Discount module

This document is the binding specification for the implementation. The public types in
`src/DiscountModule` are the contract; everything below explains how they must behave.

## 1. Scope

The module takes (a) the items in a shopping cart and (b) a list of discount campaigns with their
parameters, and returns the final price plus a step-by-step breakdown. Cart management, inventory
and checkout are out of scope. Two front ends are only adapters around the module:

- `src/DiscountModule.Cli` reads an order from a JSON file or stdin, calls the module and prints the result.
- `src/DiscountModule.Api` exposes the same thing as `POST /api/discounts`.

Both use `src/DiscountModule.Json`, which reads a JSON order into domain types (section 7) and defines the
JSON shape of a result (`PricedOrder`). The module itself knows nothing about JSON, consoles or HTTP.

## 2. Domain model

| Type | Role |
|------|------|
| `Cart`, `CartItem`, `ItemCategory` | Input cart. `CartItem.LineTotal = UnitPrice * Quantity`. `Cart.Subtotal` = sum of line totals. |
| `Campaign` (abstract record) | One campaign with its parameters. Knows its `Category` and `Name`, validates its own parameters (`Validate()`), computes its discount (`ComputeDiscount(DiscountContext)`) and explains it (`Describe(DiscountContext)`, e.g. "15% off Clothing (1,050.00 THB)"). |
| `CampaignCategory.ToDisplayName()` | "Coupon" / "On Top" / "Seasonal" for messages and output. |
| `InputLimits` | `MaxAmount` = 1,000,000,000 THB, `MaxQuantity` = 1,000,000, `MaxSubtotal` = 1,000,000,000,000,000 THB (see A11). |
| `FixedAmountCoupon`, `PercentageCoupon` | Category **Coupon** |
| `CategoryPercentageOnTop`, `PointsOnTop` | Category **On Top** |
| `SeasonalCampaign` | Category **Seasonal** |
| `DiscountContext` | `Cart` + `RunningTotal` (total after the campaigns applied so far). |
| `DiscountCalculator.Calculate(cart, campaigns)` | Entry point. Validates, orders, applies. |
| `DiscountResult` | `Subtotal`, `AppliedDiscounts` (one `AppliedDiscount` per campaign, in application order), `FinalPrice`, `TotalDiscount`, `PointsUsed`. |
| `DiscountValidationException` | Thrown by `Calculate` with **all** validation errors collected. |

Adding a campaign type = adding one derived record. The calculator is closed for modification.
Accepting the new type from JSON (CLI and API alike) takes one more entry in the `CampaignTypes` table of
`DiscountModule.Json` (section 7).

## 3. Calculation algorithm (`DiscountCalculator.Calculate`)

1. Copy the cart's items and the campaign list into arrays, and work on those copies only. A caller
   changing its lists from another thread cannot slip an unchecked value between steps 2 and 5.
2. Validate the copies (section 5). If any error → throw `DiscountValidationException` with every error found.
3. `running = Money.Round(subtotal)`. The subtotal is already exact, because monetary inputs have at
   most 2 decimals (A8); `Money.Round` only fixes its scale at 2 decimal places.
4. Sort campaigns by `CampaignCategory` (Coupon = 1, On Top = 2, Seasonal = 3). The order in which the caller passed them is irrelevant.
5. For each campaign in that order:
   - `raw = campaign.ComputeDiscount(new DiscountContext(cart, running))`
   - `discount = Money.Round(Math.Clamp(raw, 0, running))` — never more than what is left, never negative.
   - record `AppliedDiscount(campaign, priceBefore: running, discount, priceAfter: running - discount, campaign.Describe(context))`
   - `running -= discount`
6. Return `DiscountResult(subtotal, applied, finalPrice: running)`.

Invariants: `FinalPrice >= 0`, `FinalPrice <= Subtotal`, `TotalDiscount = Subtotal - FinalPrice`,
each step `PriceAfter = PriceBefore - DiscountAmount`, the input cart is never mutated. Every amount in
the result carries exactly 2 decimal places (`Money.Round` fixes the `decimal` scale), so 550 and 60.0
print as 550.00 and 60.00 however they were computed.

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
- when every item is valid: subtotal `<= 1,000,000,000,000,000` (checked only then, so one bad item
  does not also produce a subtotal error)

Campaign list
- list must not be null; may be empty (final price = subtotal); no null entries
- every campaign's `Category` must be a defined `CampaignCategory` (guards against a badly written derived campaign)
- **at most one campaign per category** — `FixedAmountCoupon` + `PercentageCoupon` is rejected,
  and so is `CategoryPercentageOnTop` + `PointsOnTop` (both are On Top)
- `FixedAmountCoupon.Amount` in (0, 1,000,000,000], at most 2 decimals
- `PercentageCoupon.Percentage` in (0, 100], at most 2 decimals
- `CategoryPercentageOnTop.Percentage` in (0, 100], at most 2 decimals; `ItemCategory` defined
- `PointsOnTop.Points >= 1`
- `SeasonalCampaign.EveryAmount` and `DiscountAmount` in (0, 1,000,000,000], at most 2 decimals; `DiscountAmount <= EveryAmount`

Bounds are checked before any arithmetic, so absurd input is reported, never an `OverflowException`.

Error messages name the offending campaign/item and the rule, e.g.
`"Only one Coupon campaign can be applied per order (got: Fixed amount, Percentage discount)"`,
`"Item #2 'Hat': quantity must be at least 1 (got 0)"`.

## 6. Assumptions (stated because the assignment leaves them open)

- **A1 — "Total price" means the running total.** Every discount is capped at the price left by the
  campaigns before it (Coupon → On Top → Seasonal). The 20% points cap and the seasonal "every X THB"
  are also calculated from that running total, not the original subtotal. The category percentage is
  the one exception, see A3.
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
  amounts, seasonal thresholds) must already be whole satang, so the subtotal is exact. Percentages
  may have at most 2 decimals too (12.5 or 33.33, not 33.333): a price with 2 decimals times a rate
  with 4 decimals has at most 6, so every intermediate product is exact and no value is rounded twice.
  Each applied discount is rounded half away from zero to 2 decimals. Prices such as 2,382.50 stay exact.
- **A9 — Items carry a quantity.** "1 T-Shirt: 350 THB" is quantity 1 × unit price 350.
- **A10 — A free item (unit price 0) is allowed; an empty cart is not.** Applying discounts to an
  empty order is treated as a mistake.
- **A11 — Supported magnitudes.** Unit prices and campaign amounts up to 1,000,000,000 THB,
  quantities up to 1,000,000 per line and an order subtotal up to 1,000,000,000,000,000 THB (one line
  at the maximum price and quantity) are supported; larger values are rejected with a message.
  Within these bounds no calculation can overflow or lose a digit: a running total has at most 18
  significant digits (16 before the point, 2 after), a percentage rate at most 5 (1.0000), so every
  product fits in the 28 digits a `decimal` holds exactly. Without the subtotal limit, an order of
  8 × 10^22 THB with a 99.99% coupon would be rounded twice and come out 0.01 THB off.
- **A12 — Item categories in the examples.** The assignment lists the items but not their categories.
  T-Shirt and Hoodie must be Clothing for the 2,382.50 example; Hat, Belt and Bag are taken as
  Accessories and Watch as Electronics. No example's result depends on these choices.

## 7. JSON input (CLI and HTTP API)

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

The CLI's `OrderRequestParser` checks the document's shape and leaves business rules to the module:

- Property names, campaign types and categories match in any letter case. Comments and trailing commas are allowed.
- `quantity` is a whole number (3, 3.0 and 3e0 are accepted) and defaults to 1 when left out. `campaigns`
  may be left out (no campaigns).
- Reported, each with its JSON path: a missing required property; an unexpected property, even one set to
  `null`; a property given twice, including in a different letter case; a value of the wrong JSON type
  (a number written as a string, `null`, a fractional quantity); a number out of range, or one a `decimal`
  cannot hold exactly (`1e-30` would silently become 0, and `100.000000000000000000000000001` would become
  100 and slip past the 2-decimal rule); an unknown campaign
  type or category, listing the valid ones. Categories match whole names only, so `"1"` or
  `"Clothing,Accessories"` are rejected.
- The shape problems in the document are reported in one pass (exit code 2). A JSON syntax error stops the
  parser at that error and is reported with its line and character column. When a campaign's `type` is
  missing or unknown, its other properties are not checked, because which ones belong depends on the type.
- When the shape is right, the module's validation errors are printed one per line (exit code 1).
  Success is 0; an unexpected failure (`CliApp.RunSafely`) prints one line and exits with 3.
- Text taken from the input (item names, unknown values, property names) is printed with control
  characters escaped (`\n`, `\t`, `\u001b`), so an item or a problem never spans two lines and cannot
  send terminal commands.
- The HTTP API answers the same two passes with status codes: 400 for shape problems, 422 for broken
  business rules, both as RFC 9457 problem details listing the problems in `errors`: all of them up to 100.
  Past 100, the JSON reader stops and ends the list with "Stopped after 100 problems; fix these first", and a
  list of broken rules ends with "... and N more problems not shown". Files, stdin and bodies are decoded as
  strict UTF-8 (a UTF-8 byte order mark is skipped); invalid bytes or a UTF-16 byte order mark are an input
  error that names the offending byte. A body that is not
  `application/json` gets 415; a body over 1 MiB gets 413, checked from `Content-Length` before reading and
  again while reading (a request may not declare its size). Success is 200 with the `PricedOrder` JSON, the
  same shape the CLI prints with `--json`.
- A string or property name holding an escaped lone surrogate (half of a UTF-16 pair) is reported as text
  that is not valid Unicode instead of crashing the reader (found by the fuzz tests).
- The campaign types live in one table (`CampaignTypes`): each entry names the type and reads its
  parameters, so accepting a new campaign type from JSON is one new entry.

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
- input cart not mutated; a list that changes after its first read cannot get an unvalidated item or
  campaign priced (the calculator works on its own copy)
- percentages with more than 2 decimals are rejected (0.001, 33.333, 0.5 - 1e-28); 0.49% of 1.00 → 0.00
- subtotal limit: two lines at the maximum price and quantity are rejected; at the limit, a discount whose
  exact value ends in .004999… is rounded down (no double rounding)
- messages and descriptions are identical under a culture that writes "12,5" (`CultureTests`)

JSON (`tests/DiscountModule.Json.Tests`)
- every campaign type and default, letter case, comments, whole numbers written as 2.0 or 1e2, and each
  shape problem from section 7 with its exact message and JSON path
- every shape problem in a document reported in one pass; malformed JSON with line and column (in
  characters, also after Thai text); numbers that a decimal cannot hold exactly; escaped and raw lone
  surrogates; empty input; missing file; a failing input stream

CLI (`tests/DiscountModule.Cli.Tests`)
- each example file produces the expected final price
- exit code 1 with every broken rule listed (`invalid-values.json`), exit code 2 with every shape problem
  listed (`invalid-format.json`), usage errors (no file, two files, unknown option, `--`)
- text output: items, breakdown and total in one aligned column, also with Thai item names; control
  characters in a name or in an error are escaped; JSON output order
- an unexpected failure (a broken output stream) exits with 3 and one line on stderr
- fuzzing: 300 damaged copies of each example file, generated from a fixed seed (deleted and inserted
  characters, extreme numbers, unusual strings, truncation). The CLI must answer every one with exit code
  0, 1 or 2, write only to the matching stream, print valid JSON with `--json`, and print no control characters

HTTP API (`tests/DiscountModule.Api.Tests`, the app running in memory through `WebApplicationFactory`)
- every example order priced (200) with the expected final price, in the same shape as the CLI output
- 422 and 400 with every problem listed, malformed JSON with its location, 415 for a non-JSON body
- 413 for a body over the limit: over HTTP with a declared size, and by calling the endpoint directly for a
  body without a declared size (the in-memory server always sends one); a declared oversize body is not read
- a UTF-8 byte order mark is accepted; other methods and paths answer with problem details

Core additions
- extensibility: an example campaign that exists only in the tests is ordered, capped, validated and counted
  by the one-per-category rule without any change to `DiscountCalculator`
- invariants: 500 random valid orders from a fixed seed; 0 <= final <= subtotal, the steps chain exactly,
  reversing the campaigns gives the same breakdown, every amount has 2 decimals, and the points used equal
  min(points, floor(20% of the price at that step))

Mutation check (`tools/mutation-check.ps1`): 33 deliberate faults, one at a time, each must make at least one
test fail.

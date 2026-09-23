using DiscountModule.Campaigns;
using DiscountModule.Cli.Input;
using DiscountModule.Models;

namespace DiscountModule.Cli.Tests;

public class OrderRequestParserTests
{
    private const string Hat = """{ "name": "Hat", "category": "Accessories", "unitPrice": 250 }""";

    private static InputFormatException ParseFails(string json)
        => Assert.Throws<InputFormatException>(() => OrderRequestParser.Parse(json));

    // ---- What is accepted ----

    [Fact]
    public void Reads_items_and_every_campaign_type()
    {
        OrderRequest request = OrderRequestParser.Parse("""
            {
              "items": [
                { "name": "T-Shirt", "category": "Clothing", "unitPrice": 350, "quantity": 2 },
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
            """);

        CartItem[] expectedItems =
        [
            new("T-Shirt", ItemCategory.Clothing, 350m, 2),
            new("Hat", ItemCategory.Accessories, 250m, 1), // quantity defaults to 1
        ];
        Campaign[] expectedCampaigns =
        [
            new FixedAmountCoupon(50m),
            new PercentageCoupon(10m),
            new CategoryPercentageOnTop(ItemCategory.Clothing, 15m),
            new PointsOnTop(68),
            new SeasonalCampaign(300m, 40m),
        ];
        Assert.Equal(expectedItems, request.Cart.Items);
        Assert.Equal(expectedCampaigns, request.Campaigns);
    }

    [Fact]
    public void Names_and_values_match_in_any_letter_case_and_comments_and_trailing_commas_are_allowed()
    {
        OrderRequest request = OrderRequestParser.Parse("""
            {
              // hand-written input files may carry comments
              "ITEMS": [ { "Name": "Hat", "CATEGORY": "accessories", "unitprice": 250, }, ],
              "Campaigns": [ { "TYPE": "points", "Points": 5 }, ],
            }
            """);

        Assert.Equal(new CartItem("Hat", ItemCategory.Accessories, 250m), Assert.Single(request.Cart.Items));
        Assert.Equal(new PointsOnTop(5), Assert.Single(request.Campaigns));
    }

    [Fact]
    public void A_whole_number_may_be_written_with_a_decimal_point_or_an_exponent()
    {
        OrderRequest request = OrderRequestParser.Parse("""
            { "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": 250, "quantity": 2.0 } ],
              "campaigns": [ { "type": "Points", "points": 1e2 } ] }
            """);

        Assert.Equal(2, Assert.Single(request.Cart.Items).Quantity);
        Assert.Equal(new PointsOnTop(100), Assert.Single(request.Campaigns));
    }

    [Fact]
    public void Leaving_out_the_campaigns_means_no_campaigns()
    {
        OrderRequest request = OrderRequestParser.Parse($$"""{ "items": [ {{Hat}} ] }""");

        Assert.Empty(request.Campaigns);
    }

    [Fact]
    public void Values_that_break_a_business_rule_are_left_for_the_module_to_report()
    {
        // A negative price or a 250% coupon is well-formed JSON; the calculator owns those rules.
        OrderRequest request = OrderRequestParser.Parse("""
            { "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": -1, "quantity": 0 } ],
              "campaigns": [ { "type": "Percentage", "percentage": 250 } ] }
            """);

        Assert.Equal(new CartItem("Hat", ItemCategory.Accessories, -1m, 0), Assert.Single(request.Cart.Items));
        Assert.Equal(new PercentageCoupon(250m), Assert.Single(request.Campaigns));
    }

    // ---- Shape problems, one at a time ----

    [Theory]
    [InlineData("""[]""", """$: expected an object, got an array""")]
    [InlineData("""42""", """$: expected an object, got the number 42""")]
    [InlineData("""null""", """$: expected an object, got null""")]
    [InlineData("""{ }""", """$: "items" is required""")]
    [InlineData("""{ "items": {} }""", """$.items: expected an array, got an object""")]
    [InlineData("""{ "items": [ 5 ] }""", """$.items[0]: expected an object, got the number 5""")]
    [InlineData("""{ "items": [ { "category": "Clothing", "unitPrice": 1 } ] }""", """$.items[0]: "name" is required""")]
    [InlineData("""{ "items": [ { "name": 7, "category": "Clothing", "unitPrice": 1 } ] }""", """$.items[0].name: expected a string, got the number 7""")]
    [InlineData("""{ "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": "250" } ] }""", """$.items[0].unitPrice: expected a number, got the string "250" """)]
    [InlineData("""{ "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": 1e30 } ] }""", """$.items[0].unitPrice: 1e30 is out of range""")]
    [InlineData("""{ "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": 250, "quantity": 1.5 } ] }""", """$.items[0].quantity: expected a whole number, got 1.5""")]
    [InlineData("""{ "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": 250, "quantity": null } ] }""", """$.items[0].quantity: expected a whole number, got null""")]
    [InlineData("""{ "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": 250, "quantity": 3000000000 } ] }""", """$.items[0].quantity: 3000000000 is out of range""")]
    [InlineData("""{ "items": [ { "name": "Hat", "category": "Shoes", "unitPrice": 250 } ] }""", """$.items[0].category: unknown value "Shoes" (expected one of: Clothing, Accessories, Electronics)""")]
    [InlineData("""{ "items": [ { "name": "Hat", "category": "Clothing,Accessories", "unitPrice": 250 } ] }""", """$.items[0].category: unknown value "Clothing,Accessories" (expected one of: Clothing, Accessories, Electronics)""")]
    [InlineData("""{ "items": [ { "name": "Hat", "category": "1", "unitPrice": 250 } ] }""", """$.items[0].category: unknown value "1" (expected one of: Clothing, Accessories, Electronics)""")]
    [InlineData("""{ "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": 250, "price": 250 } ] }""", """$.items[0]: unexpected property "price" (expected: name, category, unitPrice, quantity)""")]
    [InlineData("""{ "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": 100, "unitPrice": 1 } ] }""", """$.items[0]: property "unitPrice" is given more than once""")]
    [InlineData("""{ "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": 100, "UNITPRICE": 1 } ] }""", """$.items[0]: property "UNITPRICE" is given more than once""")]
    public void A_problem_in_the_items_is_reported_with_its_json_path(string json, string expectedError)
    {
        Assert.Equal(expectedError.TrimEnd(), Assert.Single(ParseFails(json).Errors));
    }

    [Theory]
    [InlineData("""{ "coupon": 5 }""", """$: unexpected property "coupon" (expected: items, campaigns)""")]
    [InlineData("""{ "campaigns": {} }""", """$.campaigns: expected an array, got an object""")]
    [InlineData("""{ "campaigns": [ "Points" ] }""", """$.campaigns[0]: expected an object, got the string "Points" """)]
    [InlineData("""{ "campaigns": [ { "amount": 50 } ] }""", """$.campaigns[0]: "type" is required""")]
    [InlineData("""{ "campaigns": [ { "type": 1 } ] }""", """$.campaigns[0].type: expected a string, got the number 1""")]
    [InlineData("""{ "campaigns": [ { "type": "BuyOneGetOne", "amount": 1 } ] }""", """$.campaigns[0].type: unknown value "BuyOneGetOne" (expected one of: FixedAmount, Percentage, CategoryPercentage, Points, Seasonal)""")]
    [InlineData("""{ "campaigns": [ { "type": "Seasonal", "everyAmount": 300 } ] }""", """$.campaigns[0]: "discountAmount" is required""")]
    [InlineData("""{ "campaigns": [ { "type": "CategoryPercentage", "percentage": 15 } ] }""", """$.campaigns[0]: "category" is required""")]
    [InlineData("""{ "campaigns": [ { "type": "Points", "points": 10, "amount": 50 } ] }""", """$.campaigns[0]: unexpected property "amount" (expected: type, points)""")]
    [InlineData("""{ "campaigns": [ { "type": "Points", "points": 10, "amount": null } ] }""", """$.campaigns[0]: unexpected property "amount" (expected: type, points)""")]
    [InlineData("""{ "campaigns": [ { "type": "Points", "points": 2.5 } ] }""", """$.campaigns[0].points: expected a whole number, got 2.5""")]
    [InlineData("""{ "campaigns": [ { "type": "Points", "points": 3000000000 } ] }""", """$.campaigns[0].points: 3000000000 is out of range""")]
    public void A_problem_in_the_order_or_its_campaigns_is_reported_with_its_json_path(string json, string expectedError)
    {
        // Wrap the fragment in an otherwise valid order, so the only problem is the one under test.
        string order = json.Contains("\"items\"") ? json : json.Insert(1, $$""" "items": [ {{Hat}} ], """);

        Assert.Equal(expectedError.TrimEnd(), Assert.Single(ParseFails(order).Errors));
    }

    // ---- How problems are reported ----

    [Fact]
    public void Every_shape_problem_in_the_document_is_reported_in_one_pass()
    {
        InputFormatException error = ParseFails("""
            {
              "items": [
                { "name": "Hat", "category": "Toys", "unitPrice": "250" },
                { "category": "Clothing", "unitPrice": 100, "colour": "red" }
              ],
              "campaigns": [
                { "type": "Nope" },
                { "type": "Points" }
              ]
            }
            """);

        string[] expected =
        [
            "$.items[0].category: unknown value \"Toys\" (expected one of: Clothing, Accessories, Electronics)",
            "$.items[0].unitPrice: expected a number, got the string \"250\"",
            "$.items[1]: \"name\" is required",
            "$.items[1]: unexpected property \"colour\" (expected: name, category, unitPrice, quantity)",
            "$.campaigns[0].type: unknown value \"Nope\" (expected one of: FixedAmount, Percentage, CategoryPercentage, Points, Seasonal)",
            "$.campaigns[1]: \"points\" is required",
        ];
        Assert.Equal(expected, error.Errors);
    }

    [Fact]
    public void Malformed_json_is_reported_with_its_line_and_position()
    {
        // Line 3 is missing the comma after "Hat".
        InputFormatException error = ParseFails("""
            {
              "items": [
                { "name": "Hat" "category": "Accessories" }
              ]
            }
            """);

        Assert.StartsWith("Malformed JSON at line 3, position 21: ", Assert.Single(error.Errors));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    public void Empty_input_is_rejected(string json)
    {
        Assert.Equal(
            "Input is empty; expected a JSON object with \"items\" and \"campaigns\"",
            Assert.Single(ParseFails(json).Errors));
    }

    [Fact]
    public void A_missing_file_is_reported_by_its_path()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");

        InputFormatException error = Assert.Throws<InputFormatException>(() => OrderRequestParser.ReadFile(path));

        Assert.StartsWith($"Cannot read '{path}': ", Assert.Single(error.Errors));
    }

    [Fact]
    public void A_failing_input_stream_is_reported_as_an_input_error()
    {
        InputFormatException error = Assert.Throws<InputFormatException>(() => OrderRequestParser.Read(new FailingReader()));

        Assert.Equal("Cannot read standard input: pipe closed", Assert.Single(error.Errors));
    }

    [Fact]
    public void The_exception_keeps_its_own_copy_of_the_errors()
    {
        List<string> source = ["first problem"];
        InputFormatException exception = new(source);

        source[0] = "changed later";
        source.Add("added later");

        Assert.Equal(["first problem"], exception.Errors);
        Assert.Equal("Invalid input: first problem", exception.Message);
    }

    private sealed class FailingReader : TextReader
    {
        public override string ReadToEnd() => throw new IOException("pipe closed");
    }
}

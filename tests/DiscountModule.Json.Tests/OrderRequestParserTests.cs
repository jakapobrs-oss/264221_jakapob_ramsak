using DiscountModule.Campaigns;
using DiscountModule.Json;
using DiscountModule.Models;

namespace DiscountModule.Json.Tests;

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
    public void Numbers_keep_their_exact_value_however_they_are_written()
    {
        // Trailing zeros, exponents and the largest decimal are all read without changing the value.
        OrderRequest request = OrderRequestParser.Parse("""
            { "items": [ { "name": "A", "category": "Clothing", "unitPrice": 19.90 },
                         { "name": "B", "category": "Clothing", "unitPrice": 1.25e2 },
                         { "name": "C", "category": "Clothing", "unitPrice": 5E-1 },
                         { "name": "D", "category": "Clothing", "unitPrice": 79228162514264337593543950335 },
                         { "name": "E", "category": "Clothing", "unitPrice": -0.0 } ] }
            """);

        decimal[] expected = [19.90m, 125m, 0.5m, decimal.MaxValue, 0m];
        Assert.Equal(expected, request.Cart.Items.Select(item => item.UnitPrice));
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
    [InlineData("""{ "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": 1e-30 } ] }""", """$.items[0].unitPrice: 1e-30 is too small""")]
    [InlineData("""{ "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": 100.000000000000000000000000001 } ] }""", """$.items[0].unitPrice: 100.000000000000000000000000001 has more digits than can be read exactly""")]
    [InlineData("""{ "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": 250, "quantity": 2.00000000000000000000000000001 } ] }""", """$.items[0].quantity: 2.00000000000000000000000000001 has more digits than can be read exactly""")]
    [InlineData("""{ "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": 250, "quantity": 1.5 } ] }""", """$.items[0].quantity: expected a whole number, got 1.5""")]
    [InlineData("""{ "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": 250, "quantity": 2.0000000000000000000000000001 } ] }""", """$.items[0].quantity: expected a whole number, got 2.0000000000000000000000000001""")]
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
    public void Malformed_json_is_reported_with_its_line_and_column()
    {
        // Line 3 is missing the comma after "Hat".
        InputFormatException error = ParseFails("""
            {
              "items": [
                { "name": "Hat" "category": "Accessories" }
              ]
            }
            """);

        Assert.StartsWith("Malformed JSON at line 3, column 21: ", Assert.Single(error.Errors));
    }

    [Fact]
    public void The_column_of_a_syntax_error_counts_characters_not_bytes()
    {
        // The comma after "เสื้อ" is missing. Its 5 Thai characters take 15 bytes in UTF-8, so a byte
        // count would say 42; the character column is 32.
        InputFormatException error = ParseFails("""{ "items": [ { "name": "เสื้อ" "x": 1 } ] }""");

        Assert.StartsWith("Malformed JSON at line 1, column 32: ", Assert.Single(error.Errors));
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

    // JSON escapes are assembled at run time ("\\u" + ...) so that no tool or editor turns them into raw characters.
    private static string JsonEscape(string utf16CodeUnitInHex) => "\\u" + utf16CodeUnitInHex;

    [Theory]
    [InlineData("D800")] // a high half, which must be followed by a low half
    [InlineData("DC00")] // a low half, which must follow a high half
    public void A_string_holding_half_a_surrogate_pair_is_reported_instead_of_crashing(string half)
    {
        // Found by the fuzz tests: reading this string used to throw InvalidOperationException.
        string escape = JsonEscape(half);
        string json = $$"""{ "items": [ { "name": "{{escape}}", "category": "Clothing", "unitPrice": 1 } ] }""";

        Assert.Equal(
            $"$.items[0].name: the string \"{escape}\" is not valid text (it contains an unpaired surrogate escape)",
            Assert.Single(ParseFails(json).Errors));
    }

    [Theory]
    [InlineData("D800")]
    [InlineData("DC00")]
    public void A_property_name_holding_half_a_surrogate_pair_is_reported_instead_of_crashing(string half)
    {
        string json = $$"""{ "items": [ { "{{JsonEscape(half)}}": 1, "name": "Hat", "category": "Clothing", "unitPrice": 1 } ] }""";

        Assert.Equal(
            "$.items[0]: a property name is not valid text (it contains an unpaired surrogate escape)",
            Assert.Single(ParseFails(json).Errors));
    }

    [Fact]
    public void A_whole_surrogate_pair_escape_is_read_as_the_character_it_encodes()
    {
        // Refusing half a pair must not refuse a whole one: this pair is the emoji U+1F600.
        string pair = JsonEscape("D83D") + JsonEscape("DE00");
        string json = $$"""{ "items": [ { "name": "{{pair}}", "category": "Clothing", "unitPrice": 1 } ] }""";

        OrderRequest request = OrderRequestParser.Parse(json);

        Assert.Equal(new CartItem(char.ConvertFromUtf32(0x1F600), ItemCategory.Clothing, 1m), Assert.Single(request.Cart.Items));
    }

    [Fact]
    public void Text_holding_a_raw_half_surrogate_is_reported_instead_of_crashing()
    {
        // Only possible through Parse(string); files and stdin are decoded before they get here.
        string json = "{ \"items\": [ { \"name\": \"" + (char)0xD800 + "\", \"category\": \"Clothing\", \"unitPrice\": 1 } ] }";

        Assert.Equal(
            "Input is not valid text: it contains half of a surrogate pair",
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

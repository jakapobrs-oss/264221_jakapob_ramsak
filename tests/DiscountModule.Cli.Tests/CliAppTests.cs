using System.Text.Json;

namespace DiscountModule.Cli.Tests;

/// <summary>End-to-end tests: the CLI is driven exactly like a user would, through the example files.</summary>
public class CliAppTests
{
    private static string Example(string name) => Path.Combine(AppContext.BaseDirectory, "examples", name);

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = CliApp.Run(args, stdout, stderr);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    [Theory]
    [InlineData("01-fixed-amount.json", "550.00")]
    [InlineData("02-percentage.json", "540.00")]
    [InlineData("03-category-percentage.json", "2382.50")]
    [InlineData("04-points.json", "762.00")]
    [InlineData("05-seasonal.json", "750.00")]
    [InlineData("06-all-categories.json", "567.00")]
    [InlineData("07-points-capped-after-coupon.json", "558.00")]
    [InlineData("08-coupon-then-category.json", "170.00")]
    public void Example_files_produce_the_expected_final_price(string file, string expectedFinalPrice)
    {
        var (exitCode, stdout, stderr) = Run(Example(file), "--json");

        Assert.Equal("", stderr);
        Assert.Equal(CliApp.ExitSuccess, exitCode);
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal(decimal.Parse(expectedFinalPrice), document.RootElement.GetProperty("finalPrice").GetDecimal());
    }

    [Fact]
    public void Text_output_shows_items_breakdown_and_final_price()
    {
        var (exitCode, stdout, _) = Run(Example("04-points.json"));

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        Assert.Contains("Subtotal", stdout);
        Assert.Contains("830.00", stdout);
        Assert.Contains("[On Top]", stdout);
        Assert.Contains("Final price", stdout);
        Assert.Contains("762.00", stdout);
        Assert.Contains("Points used: 68", stdout);
    }

    [Fact]
    public void Json_output_lists_discounts_in_application_order()
    {
        var (_, stdout, _) = Run(Example("06-all-categories.json"), "--json");

        using var document = JsonDocument.Parse(stdout);
        var categories = document.RootElement.GetProperty("discounts").EnumerateArray()
            .Select(discount => discount.GetProperty("category").GetString()!)
            .ToArray();
        Assert.Equal(["Coupon", "OnTop", "Seasonal"], categories);
        Assert.Equal(100, document.RootElement.GetProperty("pointsUsed").GetInt32());
    }

    [Fact]
    public void Reads_the_order_from_standard_input_when_given_a_dash()
    {
        var stdin = new StringReader(File.ReadAllText(Example("01-fixed-amount.json")));
        var stdout = new StringWriter();

        var exitCode = CliApp.Run(["-", "--json"], stdout, new StringWriter(), stdin);

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        Assert.Contains("\"finalPrice\": 550.00", stdout.ToString());
    }

    [Fact]
    public void Order_that_breaks_a_discount_rule_exits_with_1_and_explains()
    {
        var (exitCode, stdout, stderr) = Run(Example("invalid-two-coupons.json"));

        Assert.Equal(CliApp.ExitValidationError, exitCode);
        Assert.Equal("", stdout);
        Assert.StartsWith("Invalid order", stderr);
        Assert.Contains("Coupon", stderr);
    }

    [Fact]
    public void Unknown_campaign_type_exits_with_2_and_lists_supported_types()
    {
        var (exitCode, _, stderr) = Run(Example("invalid-unknown-type.json"));

        Assert.Equal(CliApp.ExitInputError, exitCode);
        Assert.StartsWith("Input error", stderr);
        Assert.Contains("BuyOneGetOne", stderr);
        Assert.Contains("Seasonal", stderr);
    }

    [Fact]
    public void Missing_file_exits_with_2()
    {
        var (exitCode, _, stderr) = Run(Example("does-not-exist.json"));

        Assert.Equal(CliApp.ExitInputError, exitCode);
        Assert.Contains("does-not-exist.json", stderr);
    }

    [Fact]
    public void Malformed_json_exits_with_2()
    {
        var stdin = new StringReader("{ not json");

        var exitCode = CliApp.Run(["-"], new StringWriter(), new StringWriter(), stdin);

        Assert.Equal(CliApp.ExitInputError, exitCode);
    }

    [Fact]
    public void No_arguments_prints_usage_and_exits_with_2()
    {
        var (exitCode, _, stderr) = Run();

        Assert.Equal(CliApp.ExitInputError, exitCode);
        Assert.Contains("Usage:", stderr);
    }

    [Fact]
    public void Unknown_option_exits_with_2()
    {
        var (exitCode, _, stderr) = Run(Example("01-fixed-amount.json"), "--verbose");

        Assert.Equal(CliApp.ExitInputError, exitCode);
        Assert.Contains("Unknown option '--verbose'", stderr);
    }

    [Fact]
    public void Help_flag_prints_usage_and_exits_with_0()
    {
        var (exitCode, stdout, _) = Run("--help");

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        Assert.Contains("Usage:", stdout);
    }

    [Fact]
    public void Absurdly_large_amounts_are_rejected_not_crashed_on()
    {
        // decimal.MaxValue x 2 would overflow; the module must refuse the value before doing any arithmetic.
        var stdin = new StringReader("""
            { "items": [ { "name": "Gold", "category": "Accessories", "unitPrice": 79228162514264337593543950335, "quantity": 2 } ],
              "campaigns": [] }
            """);
        var stderr = new StringWriter();

        var exitCode = CliApp.Run(["-"], new StringWriter(), stderr, stdin);

        Assert.Equal(CliApp.ExitValidationError, exitCode);
        Assert.StartsWith("Invalid order", stderr.ToString());
    }
}

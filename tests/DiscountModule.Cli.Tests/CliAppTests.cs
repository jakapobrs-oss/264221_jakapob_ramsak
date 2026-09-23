using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiscountModule.Cli.Tests;

/// <summary>End-to-end tests: the CLI is driven the way a user would drive it, mostly through the example files.</summary>
public class CliAppTests
{
    private static string Example(string name) => Path.Combine(AppContext.BaseDirectory, "examples", name);

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
        => RunWithInput(stdin: null, args);

    private static (int ExitCode, string Stdout, string Stderr) RunWithInput(string? stdin, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exitCode = CliApp.Run(args, stdout, stderr, stdin is null ? null : new StringReader(stdin));
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private static string[] Lines(string output) => output.TrimEnd().Split(Environment.NewLine);

    // ---- Successful runs ----

    [Theory]
    [InlineData("01-fixed-amount.json", "550.00")]
    [InlineData("02-percentage.json", "540.00")]
    [InlineData("03-category-percentage.json", "2382.50")]
    [InlineData("04-points.json", "762.00")]
    [InlineData("05-seasonal.json", "750.00")]
    [InlineData("06-all-categories.json", "567.00")]
    [InlineData("07-points-capped-after-coupon.json", "558.00")]
    [InlineData("08-coupon-then-category.json", "170.00")]
    [InlineData("09-category-capped-after-coupon.json", "500.00")]
    public void Example_files_produce_the_expected_final_price(string file, string expectedFinalPrice)
    {
        var (exitCode, stdout, stderr) = Run(Example(file), "--json");

        Assert.Equal("", stderr);
        Assert.Equal(CliApp.ExitSuccess, exitCode);
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Equal(decimal.Parse(expectedFinalPrice), document.RootElement.GetProperty("finalPrice").GetDecimal());
    }

    [Fact]
    public void Text_output_lists_the_items_the_discounts_and_the_final_price_in_one_aligned_column()
    {
        var (exitCode, stdout, _) = Run(Example("04-points.json"));
        string[] lines = Lines(stdout);

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        Assert.Equal("Items", lines[0]);
        Assert.Matches(@"^  1 x T-Shirt \(Clothing\) @ 350\.00 +350\.00$", lines[1]);
        Assert.Matches(@"^  1 x Hat \(Accessories\) @ 250\.00 +250\.00$", lines[2]);
        Assert.Matches(@"^  1 x Belt \(Accessories\) @ 230\.00 +230\.00$", lines[3]);
        Assert.Matches(@"^Subtotal +830\.00$", lines[4]);
        Assert.Matches(@"^  \[On Top\] 68 points \(cap 166 = 20% of 830\.00 THB\) +-68\.00$", lines[7]);
        Assert.Matches(@"^Total discount +-68\.00$", lines[9]);
        Assert.Matches(@"^Final price +762\.00$", lines[10]);
        Assert.Equal("Points used: 68", lines[11]);

        // Every amount ends in the same column.
        string[] amountLines = [.. lines.Where(line => Regex.IsMatch(line, @"\d\.\d\d$"))];
        Assert.Equal(7, amountLines.Length);
        Assert.Single(amountLines.Select(line => line.Length).Distinct());
    }

    [Fact]
    public void Thai_item_names_keep_the_amount_column_aligned()
    {
        const string order = """
            { "items": [ { "name": "เสื้อยืด", "category": "Clothing", "unitPrice": 350 },
                         { "name": "Hat", "category": "Accessories", "unitPrice": 250 } ] }
            """;

        var (exitCode, stdout, _) = RunWithInput(order, "-");
        string[] lines = Lines(stdout);

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        Assert.Matches(@"^  1 x เสื้อยืด \(Clothing\) @ 350\.00 +350\.00$", lines[1]);
        Assert.Matches(@"^  1 x Hat \(Accessories\) @ 250\.00 +250\.00$", lines[2]);
        Assert.Matches(@"^Subtotal +600\.00$", lines[3]);

        // "เสื้อยืด" is 8 UTF-16 characters but 5 columns wide: its 3 vowel and tone marks sit above the
        // consonants. The Thai line therefore has 3 more characters than the Hat line of the same width.
        Assert.Equal(lines[2].Length + 3, lines[1].Length);

        // On screen, every amount ends in the same column.
        string[] amountLines = [.. lines.Where(line => Regex.IsMatch(line, @"\d\.\d\d$"))];
        Assert.Equal(5, amountLines.Length); // 2 items, subtotal, total discount, final price
        Assert.Single(amountLines.Select(ScreenColumns).Distinct());
    }

    /// <summary>Columns a line takes on screen: combining marks (Thai vowels and tone marks above or below) take none.</summary>
    private static int ScreenColumns(string line)
        => line.Count(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark);

    [Fact]
    public void A_very_long_item_name_is_shortened_instead_of_widening_every_line()
    {
        string order = $$"""{ "items": [ { "name": "{{new string('A', 10_000)}}", "category": "Clothing", "unitPrice": 350 } ] }""";

        var (exitCode, stdout, _) = RunWithInput(order, "-");
        string[] lines = Lines(stdout);

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        Assert.Matches(@"^  1 x A{59}… \(Clothing\) @ 350\.00 +350\.00$", lines[1]);
        Assert.All(lines, line => Assert.True(line.Length <= 100, $"a line has {line.Length} characters"));
    }

    [Fact]
    public void A_long_Thai_name_is_shortened_by_columns_and_keeps_the_marks_of_its_last_letter()
    {
        // "เสื้อ" is 5 characters but 3 columns, so 30 of them are 90 columns: 59 are kept, then the ellipsis.
        string name = string.Concat(Enumerable.Repeat("เสื้อ", 30));
        string order = $$"""{ "items": [ { "name": "{{name}}", "category": "Clothing", "unitPrice": 350 } ] }""";

        var (exitCode, stdout, _) = RunWithInput(order, "-");
        string line = Lines(stdout)[1];
        string printedName = line["  1 x ".Length..line.IndexOf(" (Clothing)", StringComparison.Ordinal)];

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        Assert.EndsWith("…", printedName);
        Assert.Equal(60, ScreenColumns(printedName));
        Assert.StartsWith(printedName[..^1], name, StringComparison.Ordinal); // the tone mark of the last letter kept is still there
        Assert.EndsWith("เสื้…", printedName, StringComparison.Ordinal);
    }

    [Fact]
    public void Control_characters_in_an_item_name_are_escaped_so_every_line_stays_whole()
    {
        // The JSON string "Line\nbreak" holds a real newline, and ESC could recolour the terminal.
        string name = JsonSerializer.Serialize("Line\nbreak" + (char)27 + "[31m");
        string order = $$"""{ "items": [ { "name": {{name}}, "category": "Clothing", "unitPrice": 350 } ] }""";

        var (exitCode, stdout, _) = RunWithInput(order, "-");
        string[] lines = Lines(stdout);

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        Assert.DoesNotContain((char)27, stdout);
        Assert.StartsWith(@"  1 x Line\nbreak\u001b[31m (Clothing) @ 350.00", lines[1]);
        Assert.Matches(@"^Subtotal +350\.00$", lines[2]);
    }

    [Fact]
    public void Control_characters_quoted_in_an_error_keep_it_on_one_line()
    {
        string name = JsonSerializer.Serialize("Bad\nname");
        string order = $$"""{ "items": [ { "name": {{name}}, "category": "Clothing", "unitPrice": -1 } ] }""";

        var (exitCode, _, stderr) = RunWithInput(order, "-");

        Assert.Equal(CliApp.ExitValidationError, exitCode);
        Assert.Equal(
            ["Invalid order (1):", @"  - Item #1 'Bad\nname': unit price must not be negative (got -1)"],
            Lines(stderr));
    }

    [Fact]
    public void Direction_overrides_in_an_item_name_are_escaped_so_they_cannot_reorder_the_line()
    {
        // "Trojan Source": the right-to-left override U+202E would show the rest of the line reversed, so a
        // name could make the amount column read differently on screen.
        const char RightToLeftOverride = (char)0x202E;
        string name = JsonSerializer.Serialize("Coat" + RightToLeftOverride + "00.01");
        string order = $$"""{ "items": [ { "name": {{name}}, "category": "Clothing", "unitPrice": 350 } ] }""";

        var (exitCode, stdout, _) = RunWithInput(order, "-");

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        Assert.DoesNotContain(RightToLeftOverride, stdout);
        Assert.StartsWith("  1 x Coat" + @"\" + "u202e00.01 (Clothing) @ 350.00", Lines(stdout)[1]);
    }

    [Fact]
    public void Zero_width_characters_of_real_text_are_printed_as_they_are()
    {
        // A zero width space marks a word break in Thai; a zero width joiner builds an emoji sequence.
        string text = "เสื้อ" + (char)0x200B + "ยืด " + char.ConvertFromUtf32(0x1F469) + (char)0x200D + char.ConvertFromUtf32(0x1F4BB);
        string order = $$"""{ "items": [ { "name": {{JsonSerializer.Serialize(text)}}, "category": "Clothing", "unitPrice": 350 } ] }""";

        var (exitCode, stdout, _) = RunWithInput(order, "-");

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        Assert.StartsWith($"  1 x {text} (Clothing) @ 350.00", Lines(stdout)[1], StringComparison.Ordinal);
    }

    [Fact]
    public void An_unexpected_failure_exits_with_3_and_one_line_instead_of_a_stack_trace()
    {
        var stderr = new StringWriter();

        int exitCode = CliApp.RunSafely([Example("01-fixed-amount.json")], new BrokenWriter(), stderr);

        Assert.Equal(CliApp.ExitUnexpectedError, exitCode);
        Assert.Equal("Unexpected error: IOException: pipe closed", stderr.ToString().TrimEnd());
    }

    /// <summary>An output that fails like a closed pipe.</summary>
    private sealed class BrokenWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void Write(char value) => throw new IOException("pipe closed");

        public override void Write(string? value) => throw new IOException("pipe closed");

        public override void WriteLine(string? value) => throw new IOException("pipe closed");
    }

    [Fact]
    public void Json_output_lists_the_discounts_in_application_order()
    {
        var (_, stdout, _) = Run(Example("06-all-categories.json"), "--json");

        using JsonDocument document = JsonDocument.Parse(stdout);
        string[] categories =
        [
            .. document.RootElement.GetProperty("discounts").EnumerateArray()
                .Select(discount => discount.GetProperty("category").GetString()!),
        ];
        Assert.Equal(["Coupon", "OnTop", "Seasonal"], categories);
        Assert.Equal(100, document.RootElement.GetProperty("pointsUsed").GetInt32());
    }

    [Fact]
    public void Reads_the_order_from_standard_input_when_given_a_dash()
    {
        var (exitCode, stdout, _) = RunWithInput(File.ReadAllText(Example("01-fixed-amount.json")), "-", "--json");

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        Assert.Contains("\"finalPrice\": 550.00", stdout);
    }

    [Fact]
    public void Help_prints_usage_and_exits_with_0()
    {
        var (exitCode, stdout, _) = Run("--help");

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        Assert.Contains("Usage:", stdout);
    }

    // ---- Exit code 1: the order breaks a discount rule ----

    [Fact]
    public void Two_coupons_exit_with_1_and_explain_the_rule()
    {
        var (exitCode, stdout, stderr) = Run(Example("invalid-two-coupons.json"));

        Assert.Equal(CliApp.ExitValidationError, exitCode);
        Assert.Equal("", stdout);
        Assert.Equal(
            [
                "Invalid order (1):",
                "  - Only one Coupon campaign can be applied per order (got: Fixed amount, Percentage discount)",
            ],
            Lines(stderr));
    }

    [Fact]
    public void Every_broken_rule_in_the_order_is_reported_at_once()
    {
        var (exitCode, _, stderr) = Run(Example("invalid-values.json"));

        Assert.Equal(CliApp.ExitValidationError, exitCode);
        Assert.Equal(
            [
                "Invalid order (6):",
                "  - Item #1 'T-Shirt': unit price must not be negative (got -350)",
                "  - Item #2 'Hat': unit price must have at most 2 decimal places (got 250.005)",
                "  - Item #2 'Hat': quantity must be at least 1 (got 0)",
                "  - Campaign #2 'Percentage discount': percentage must be greater than 0 and at most 100 (got 150)",
                "  - Campaign #3 'Special campaign': discount amount must not exceed every amount (got 400 > 300)",
                "  - Only one Coupon campaign can be applied per order (got: Fixed amount, Percentage discount)",
            ],
            Lines(stderr));
    }

    [Fact]
    public void Absurdly_large_amounts_are_rejected_not_crashed_on()
    {
        // decimal.MaxValue x 2 would overflow; the module refuses the value before doing any arithmetic.
        const string order = """
            { "items": [ { "name": "Gold", "category": "Accessories", "unitPrice": 79228162514264337593543950335, "quantity": 2 } ] }
            """;

        var (exitCode, _, stderr) = RunWithInput(order, "-");

        Assert.Equal(CliApp.ExitValidationError, exitCode);
        Assert.Contains("unit price is too large", stderr);
    }

    // ---- Exit code 2: the input cannot be read or understood ----

    [Fact]
    public void Every_shape_problem_in_the_file_is_reported_at_once_with_exit_code_2()
    {
        var (exitCode, stdout, stderr) = Run(Example("invalid-format.json"));

        Assert.Equal(CliApp.ExitInputError, exitCode);
        Assert.Equal("", stdout);
        Assert.Equal(
            [
                "Input error (5):",
                "  - $.items[0].category: unknown value \"Shirts\" (expected one of: Clothing, Accessories, Electronics)",
                "  - $.items[1]: \"unitPrice\" is required",
                "  - $.items[1]: unexpected property \"price\" (expected: name, category, unitPrice, quantity)",
                "  - $.campaigns[0].percentage: expected a number, got the string \"10\"",
                "  - $.campaigns[1].type: unknown value \"BuyOneGetOne\" (expected one of: FixedAmount, Percentage, CategoryPercentage, Points, Seasonal)",
            ],
            Lines(stderr));
    }

    [Fact]
    public void Malformed_json_exits_with_2_and_says_where()
    {
        var (exitCode, _, stderr) = RunWithInput("{ not json", "-");

        Assert.Equal(CliApp.ExitInputError, exitCode);
        Assert.StartsWith("Input error (1):" + Environment.NewLine + "  - Malformed JSON at line 1, column 3: ", stderr);
    }

    [Fact]
    public void A_missing_file_exits_with_2()
    {
        var (exitCode, _, stderr) = Run(Example("does-not-exist.json"));

        Assert.Equal(CliApp.ExitInputError, exitCode);
        Assert.Contains("does-not-exist.json", stderr);
    }

    [Fact]
    public void A_directory_given_as_the_order_file_exits_with_2_and_says_it_is_a_directory()
    {
        var (exitCode, _, stderr) = Run(AppContext.BaseDirectory);

        Assert.Equal(CliApp.ExitInputError, exitCode);
        Assert.Contains("it is a directory", stderr);
    }

    [Fact]
    public void No_arguments_prints_usage_and_exits_with_2()
    {
        var (exitCode, _, stderr) = Run();

        Assert.Equal(CliApp.ExitInputError, exitCode);
        Assert.StartsWith("Usage:", stderr.TrimStart());
    }

    [Fact]
    public void An_unknown_option_exits_with_2()
    {
        var (exitCode, _, stderr) = Run(Example("01-fixed-amount.json"), "--verbose");

        Assert.Equal(CliApp.ExitInputError, exitCode);
        Assert.StartsWith("Unknown option '--verbose'", stderr);
    }

    [Fact]
    public void Two_order_files_exit_with_2()
    {
        var (exitCode, _, stderr) = Run(Example("01-fixed-amount.json"), Example("02-percentage.json"));

        Assert.Equal(CliApp.ExitInputError, exitCode);
        Assert.StartsWith("Only one order file can be given", stderr);
    }

    [Fact]
    public void After_a_double_dash_an_argument_starting_with_a_dash_is_a_file_name()
    {
        var (exitCode, _, stderr) = Run("--", "-order.json");

        Assert.Equal(CliApp.ExitInputError, exitCode);
        Assert.Contains("Cannot read '-order.json'", stderr);
    }
}

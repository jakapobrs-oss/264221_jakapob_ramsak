using DiscountModule.Cli.Input;

namespace DiscountModule.Cli.Tests;

public class OrderRequestReaderTests
{
    [Fact]
    public void Parses_properties_case_insensitively_with_comments_and_trailing_commas()
    {
        const string json = """
            {
              // comments are tolerated in hand-written input files
              "ITEMS": [ { "name": "Hat", "CATEGORY": "Accessories", "unitprice": 250, }, ],
              "campaigns": [ { "type": "Points", "points": 5 }, ]
            }
            """;

        var dto = OrderRequestReader.Parse(json);

        var item = Assert.Single(dto.Items!);
        Assert.Equal(("Hat", "Accessories", 250m, (int?)null), (item.Name, item.Category, item.UnitPrice, item.Quantity));
        Assert.Equal(5, Assert.Single(dto.Campaigns!).Points);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("null")]
    public void Rejects_empty_or_null_documents(string json)
    {
        var error = Assert.Throws<InputFormatException>(() => OrderRequestReader.Parse(json));

        Assert.Contains("expected a JSON object", Assert.Single(error.Errors));
    }

    [Fact]
    public void Reports_malformed_json_with_its_location()
    {
        var error = Assert.Throws<InputFormatException>(() => OrderRequestReader.Parse("{ \"items\": [ { \"name\": \"Hat\" "));

        Assert.StartsWith("Malformed JSON", Assert.Single(error.Errors));
    }

    [Fact]
    public void Rejects_numbers_written_as_strings_and_points_at_the_field()
    {
        var error = Assert.Throws<InputFormatException>(() =>
            OrderRequestReader.Parse("""{ "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": "250" } ] }"""));

        Assert.Contains("$.items[0].unitPrice", Assert.Single(error.Errors));
    }

    [Fact]
    public void Rejects_unknown_properties_to_catch_typos()
    {
        var error = Assert.Throws<InputFormatException>(() =>
            OrderRequestReader.Parse("""{ "items": [ { "name": "Hat", "category": "Accessories", "price": 250 } ] }"""));

        Assert.Contains("price", Assert.Single(error.Errors));
    }

    [Fact]
    public void Rejects_a_top_level_array()
    {
        var error = Assert.Throws<InputFormatException>(() => OrderRequestReader.Parse("[]"));

        Assert.StartsWith("Malformed JSON", Assert.Single(error.Errors));
    }

    [Fact]
    public void Reports_missing_file_by_path()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");

        var error = Assert.Throws<InputFormatException>(() => OrderRequestReader.ReadFile(path));

        Assert.Contains(path, Assert.Single(error.Errors));
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using DiscountModule.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DiscountModule.Api.Tests;

/// <summary>The API answers with a bounded number of problems, and only accepts UTF-8.</summary>
public class ProblemLimitTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Endpoint = "/api/discounts";
    private const string ValidOrder = """{ "items": [ { "name": "Hat", "category": "Accessories", "unitPrice": 250 } ] }""";

    private readonly HttpClient client = factory.CreateClient();

    private async Task<(HttpStatusCode Status, string[] Errors)> PostAsync(HttpContent content)
    {
        HttpResponseMessage response = await client.PostAsync(Endpoint, content);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string[] errors = body.RootElement.TryGetProperty("errors", out JsonElement list)
            ? [.. list.EnumerateArray().Select(error => error.GetString()!)]
            : [];
        return (response.StatusCode, errors);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Many_broken_rules_are_422_with_the_first_100_and_a_count()
    {
        string items = string.Join(", ", Enumerable.Range(1, 1_000)
            .Select(n => $"{{ \"name\": \"Item {n}\", \"category\": \"Clothing\", \"unitPrice\": -1 }}"));

        (HttpStatusCode status, string[] errors) = await PostAsync(Json($"{{ \"items\": [{items}] }}"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal(ProblemList.MaxReportedProblems + 1, errors.Length);
        Assert.Equal("... and 900 more problems not shown", errors[^1]);
    }

    [Fact]
    public async Task A_huge_broken_order_is_400_with_the_first_100_and_a_stop_notice()
    {
        string json = "{ \"items\": [" + string.Join(", ", Enumerable.Repeat("{}", 1_000)) + "] }";

        (HttpStatusCode status, string[] errors) = await PostAsync(Json(json));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(ProblemList.MaxReportedProblems + 1, errors.Length);
        Assert.Equal(ProblemList.StoppedMessage, errors[^1]);
    }

    [Fact]
    public async Task A_charset_other_than_utf8_is_415()
    {
        // StringContent with Encoding.Unicode sends UTF-16 bytes and "charset=utf-16".
        (HttpStatusCode status, string[] errors) = await PostAsync(new StringContent(ValidOrder, Encoding.Unicode, "application/json"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, status);
        Assert.Contains("utf-16", Assert.Single(errors));
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("UTF-8")]
    [InlineData("\"utf-8\"")]
    public async Task A_utf8_charset_in_any_spelling_is_accepted(string charset)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(ValidOrder));
        content.Headers.TryAddWithoutValidation("Content-Type", $"application/json; charset={charset}");

        HttpResponseMessage response = await client.PostAsync(Endpoint, content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

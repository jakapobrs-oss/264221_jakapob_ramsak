using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DiscountModule.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DiscountModule.Api.Tests;

/// <summary>Integration tests: the whole app runs in memory and is called over HTTP, like a real client would.</summary>
public class DiscountEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Endpoint = "/api/discounts";

    private readonly HttpClient client = factory.CreateClient();

    private static string Example(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "examples", name));

    private Task<HttpResponseMessage> PostAsync(string body, string mediaType = "application/json")
        => client.PostAsync(Endpoint, new StringContent(body, Encoding.UTF8, mediaType));

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string[] Errors(JsonElement problem)
        => [.. problem.GetProperty("errors").EnumerateArray().Select(error => error.GetString()!)];

    [Theory]
    [InlineData("01-fixed-amount.json", "550.00")]
    [InlineData("02-percentage.json", "540.00")]
    [InlineData("03-category-percentage.json", "2382.50")]
    [InlineData("04-points.json", "762.00")]
    [InlineData("05-seasonal.json", "750.00")]
    [InlineData("06-all-categories.json", "567.00")]
    [InlineData("07-points-capped-after-coupon.json", "558.00")]
    [InlineData("08-coupon-then-category.json", "170.00")]
    public async Task Example_orders_are_priced(string file, string expectedFinalPrice)
    {
        HttpResponseMessage response = await PostAsync(Example(file));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        JsonElement result = await ReadJsonAsync(response);
        Assert.Equal(decimal.Parse(expectedFinalPrice), result.GetProperty("finalPrice").GetDecimal());
    }

    [Fact]
    public async Task The_result_has_the_same_shape_as_the_cli_json_output()
    {
        HttpResponseMessage response = await PostAsync(Example("07-points-capped-after-coupon.json"));

        JsonElement result = await ReadJsonAsync(response);
        Assert.Equal(830.00m, result.GetProperty("subtotal").GetDecimal());
        Assert.Equal(149, result.GetProperty("pointsUsed").GetInt32());
        Assert.Equal(272.00m, result.GetProperty("totalDiscount").GetDecimal());
        string[] categories = [.. result.GetProperty("discounts").EnumerateArray().Select(step => step.GetProperty("category").GetString()!)];
        Assert.Equal(["Coupon", "OnTop", "Seasonal"], categories);
    }

    [Fact]
    public async Task An_order_that_breaks_a_discount_rule_is_422_with_every_problem()
    {
        HttpResponseMessage response = await PostAsync(Example("invalid-values.json"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        JsonElement problem = await ReadJsonAsync(response);
        Assert.Equal("The order breaks a discount rule", problem.GetProperty("title").GetString());
        Assert.Equal(6, Errors(problem).Length);
        Assert.Contains("Only one Coupon campaign can be applied per order (got: Fixed amount, Percentage discount)", Errors(problem));
    }

    [Fact]
    public async Task An_order_with_the_wrong_shape_is_400_with_every_problem()
    {
        HttpResponseMessage response = await PostAsync(Example("invalid-format.json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement problem = await ReadJsonAsync(response);
        Assert.Equal("The order could not be read", problem.GetProperty("title").GetString());
        Assert.Equal(5, Errors(problem).Length);
    }

    [Fact]
    public async Task Malformed_json_is_400_and_says_where()
    {
        HttpResponseMessage response = await PostAsync("{ not json");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.StartsWith("Malformed JSON at line 1, column 3: ", Assert.Single(Errors(await ReadJsonAsync(response))));
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_415()
    {
        HttpResponseMessage response = await PostAsync(Example("01-fixed-amount.json"), "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task A_body_over_the_size_limit_is_413_before_it_is_parsed()
    {
        string huge = "{ \"items\": [], \"padding\": \"" + new string('x', DiscountEndpoint.MaxOrderBytes) + "\" }";

        HttpResponseMessage response = await PostAsync(huge);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task A_body_without_a_declared_size_is_still_limited()
    {
        // No Content-Length header (as with chunked transfer encoding): the limit must hold while reading.
        // The in-memory test server always sends a length, so this calls the endpoint directly.
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(new byte[DiscountEndpoint.MaxOrderBytes + 1]);

        IResult result = await DiscountEndpoint.PriceAsync(context.Request, CancellationToken.None);

        Assert.Null(context.Request.ContentLength);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Fact]
    public async Task A_declared_size_over_the_limit_is_refused_without_reading_the_body()
    {
        var body = new MemoryStream(Encoding.UTF8.GetBytes(Example("01-fixed-amount.json")));
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = DiscountEndpoint.MaxOrderBytes + 1;
        context.Request.Body = body;

        IResult result = await DiscountEndpoint.PriceAsync(context.Request, CancellationToken.None);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(0, body.Position);
    }

    [Fact]
    public async Task A_utf8_byte_order_mark_is_accepted()
    {
        byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Example("01-fixed-amount.json"))];
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        HttpResponseMessage response = await client.PostAsync(Endpoint, content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Other_methods_and_paths_answer_with_problem_details()
    {
        HttpResponseMessage get = await client.GetAsync(Endpoint);
        HttpResponseMessage unknown = await client.PostAsync("/api/unknown", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
    }
}

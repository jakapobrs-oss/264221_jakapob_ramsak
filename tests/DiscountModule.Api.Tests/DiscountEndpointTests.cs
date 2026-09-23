using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using DiscountModule.Api;
using DiscountModule.Cli;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DiscountModule.Api.Tests;

/// <summary>Integration tests: the whole app runs in memory and is called over HTTP, like a real client would.</summary>
public class DiscountEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Endpoint = "/api/discounts";

    /// <summary>DiscountEndpoint reads a body 16 KiB at a time, so it may read one buffer past the limit, no more.</summary>
    private const int EndpointReadBufferBytes = 16_384;

    /// <summary>A Thai letter: 1 character, 3 bytes in UTF-8.</summary>
    private const char ThaiLetter = 'ก';

    private readonly HttpClient client = factory.CreateClient();

    private static string Example(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "examples", name));

    private Task<HttpResponseMessage> PostAsync(string body, string mediaType = "application/json")
        => client.PostAsync(Endpoint, new StringContent(body, Encoding.UTF8, mediaType));

    /// <summary>Posts raw bytes as application/json with no charset, as a client sending a file would.</summary>
    private Task<HttpResponseMessage> PostBytesAsync(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return client.PostAsync(Endpoint, content);
    }

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

    [Theory]
    [InlineData("01-fixed-amount.json")]
    [InlineData("02-percentage.json")]
    [InlineData("03-category-percentage.json")]
    [InlineData("04-points.json")]
    [InlineData("05-seasonal.json")]
    [InlineData("06-all-categories.json")]
    [InlineData("07-points-capped-after-coupon.json")]
    [InlineData("08-coupon-then-category.json")]
    public async Task The_result_is_the_json_the_cli_prints_and_has_every_documented_field(string file)
    {
        string order = Example(file);

        HttpResponseMessage response = await PostAsync(order);
        string apiJson = await response.Content.ReadAsStringAsync();
        string cliJson = RunCliWithJsonOutput(order);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(apiJson), JsonNode.Parse(cliJson)),
            $"The API and the CLI disagree.{Environment.NewLine}API: {apiJson}{Environment.NewLine}CLI: {cliJson}");

        // Both front ends share one result type, so a field missing from both would still compare equal above.
        using JsonDocument document = JsonDocument.Parse(apiJson);
        JsonElement result = document.RootElement;
        AssertFieldNames(["subtotal", "discounts", "totalDiscount", "pointsUsed", "finalPrice"], result);
        JsonElement[] steps = [.. result.GetProperty("discounts").EnumerateArray()];
        Assert.NotEmpty(steps);
        foreach (JsonElement step in steps)
        {
            AssertFieldNames(["category", "campaign", "description", "priceBefore", "discountAmount", "priceAfter"], step);
        }
    }

    [Fact]
    public async Task An_order_that_breaks_a_discount_rule_is_422_with_every_problem()
    {
        HttpResponseMessage response = await PostAsync(Example("invalid-values.json"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        JsonElement problem = await ReadJsonAsync(response);
        Assert.Equal("The order breaks a discount rule", problem.GetProperty("title").GetString());
        Assert.Equal(
            [
                "Item #1 'T-Shirt': unit price must not be negative (got -350)",
                "Item #2 'Hat': unit price must have at most 2 decimal places (got 250.005)",
                "Item #2 'Hat': quantity must be at least 1 (got 0)",
                "Campaign #2 'Percentage discount': percentage must be greater than 0 and at most 100 (got 150)",
                "Campaign #3 'Special campaign': discount amount must not exceed every amount (got 400 > 300)",
                "Only one Coupon campaign can be applied per order (got: Fixed amount, Percentage discount)",
            ],
            Errors(problem));
    }

    [Fact]
    public async Task An_order_with_the_wrong_shape_is_400_with_every_problem()
    {
        HttpResponseMessage response = await PostAsync(Example("invalid-format.json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        JsonElement problem = await ReadJsonAsync(response);
        Assert.Equal("The order could not be read", problem.GetProperty("title").GetString());
        Assert.Equal(
            [
                "$.items[0].category: unknown value \"Shirts\" (expected one of: Clothing, Accessories, Electronics)",
                "$.items[1]: \"unitPrice\" is required",
                "$.items[1]: unexpected property \"price\" (expected: name, category, unitPrice, quantity)",
                "$.campaigns[0].percentage: expected a number, got the string \"10\"",
                "$.campaigns[1].type: unknown value \"BuyOneGetOne\" (expected one of: FixedAmount, Percentage, CategoryPercentage, Points, Seasonal)",
            ],
            Errors(problem));
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

    [Theory]
    [InlineData(DiscountEndpoint.MaxOrderBytes - 1)]
    [InlineData(DiscountEndpoint.MaxOrderBytes)]
    public async Task A_body_up_to_the_size_limit_is_priced(int bytes)
    {
        HttpResponseMessage response = await PostAsync(ValidOrderOfUtf8Bytes(bytes));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100.00m, (await ReadJsonAsync(response)).GetProperty("finalPrice").GetDecimal());
    }

    [Fact]
    public async Task A_body_one_byte_over_the_size_limit_is_413()
    {
        HttpResponseMessage response = await PostAsync(ValidOrderOfUtf8Bytes(DiscountEndpoint.MaxOrderBytes + 1));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Theory]
    [InlineData(DiscountEndpoint.MaxOrderBytes + 1)]
    [InlineData(10 * 1024 * 1024)]
    public async Task A_body_without_a_declared_size_is_read_at_most_one_buffer_past_the_limit(int bodyBytes)
    {
        // No Content-Length header (as with chunked transfer encoding): the limit must hold while reading.
        // The in-memory test server always sends a length, so this calls the endpoint directly.
        var body = new CountingStream(bodyBytes);
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = body;

        IResult result = await DiscountEndpoint.PriceAsync(context.Request, CancellationToken.None);

        Assert.Null(context.Request.ContentLength);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.InRange(body.BytesRead, DiscountEndpoint.MaxOrderBytes + 1, DiscountEndpoint.MaxOrderBytes + EndpointReadBufferBytes);
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

        HttpResponseMessage response = await PostBytesAsync(bytes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(550.00m, (await ReadJsonAsync(response)).GetProperty("finalPrice").GetDecimal());
    }

    [Fact]
    public async Task A_body_that_is_not_utf8_is_400_and_gives_the_offset()
    {
        const string BeforeName = "{ \"items\": [ { \"name\": \"";
        byte[] bytes = [.. Encoding.UTF8.GetBytes(BeforeName), 0xFF, .. Encoding.UTF8.GetBytes("\", \"category\": \"Clothing\", \"unitPrice\": 1 } ] }")];

        HttpResponseMessage response = await PostBytesAsync(bytes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement problem = await ReadJsonAsync(response);
        Assert.Equal("The order could not be read", problem.GetProperty("title").GetString());
        Assert.Equal([$"Input is not valid UTF-8 text (byte 0xFF at offset {BeforeName.Length})"], Errors(problem));
    }

    [Fact]
    public async Task A_utf16_body_without_a_charset_is_400_even_with_a_byte_order_mark()
    {
        byte[] bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Example("01-fixed-amount.json"))];

        HttpResponseMessage response = await PostBytesAsync(bytes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ["Input is UTF-16 text (it starts with a UTF-16 byte order mark); it must be UTF-8"],
            Errors(await ReadJsonAsync(response)));
    }

    [Fact]
    public async Task Other_methods_and_paths_answer_with_problem_details()
    {
        HttpResponseMessage get = await client.GetAsync(Endpoint);
        HttpResponseMessage unknown = await client.PostAsync("/api/unknown", new StringContent("{}", Encoding.UTF8, "application/json"));

        await AssertProblemDetailsAsync(get, HttpStatusCode.MethodNotAllowed, "Method Not Allowed");
        await AssertProblemDetailsAsync(unknown, HttpStatusCode.NotFound, "Not Found");
    }

    private static string RunCliWithJsonOutput(string order)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int exitCode = CliApp.Run(["-", "--json"], stdout, stderr, new StringReader(order));

        Assert.True(exitCode == CliApp.ExitSuccess, $"The CLI exited with {exitCode}: {stderr}");
        return stdout.ToString();
    }

    /// <summary>Checks the property names of a JSON object, in any order.</summary>
    private static void AssertFieldNames(string[] expected, JsonElement jsonObject)
        => Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            jsonObject.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

    private static async Task AssertProblemDetailsAsync(HttpResponseMessage response, HttpStatusCode status, string title)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        JsonElement problem = await ReadJsonAsync(response);
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.Equal(title, problem.GetProperty("title").GetString());
    }

    /// <summary>
    /// A valid order (one item at 100.00 THB) that is exactly <paramref name="bytes"/> bytes long in UTF-8. The name
    /// is padded with Thai letters, 3 bytes each, so the body has about a third as many characters as bytes.
    /// </summary>
    private static string ValidOrderOfUtf8Bytes(int bytes)
    {
        const string Start = "{ \"items\": [ { \"name\": \"";
        const string End = "\", \"category\": \"Clothing\", \"unitPrice\": 100, \"quantity\": 1 } ], \"campaigns\": [] }";

        int nameBytes = bytes - Encoding.UTF8.GetByteCount(Start + End);
        string name = new string(ThaiLetter, nameBytes / 3) + new string('x', nameBytes % 3);
        string order = Start + name + End;

        Assert.Equal(bytes, Encoding.UTF8.GetByteCount(order));
        return order;
    }

    /// <summary>
    /// A request body of <c>length</c> bytes of Thai text, made while it is read, that counts the bytes read from it.
    /// A limit counted in characters instead of bytes would read about three times the limit before it stopped.
    /// </summary>
    private sealed class CountingStream(int length) : Stream
    {
        private static readonly byte[] ThaiLetterUtf8 = Encoding.UTF8.GetBytes(ThaiLetter.ToString());

        public int BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int count = Math.Min(buffer.Length, length - BytesRead);
            for (int i = 0; i < count; i++)
            {
                buffer[i] = ThaiLetterUtf8[(BytesRead + i) % ThaiLetterUtf8.Length];
            }

            BytesRead += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

using DiscountModule.Json;
using DiscountModule.Validation;

using Microsoft.Net.Http.Headers;

using static System.FormattableString;

namespace DiscountModule.Api;

/// <summary>
/// POST /api/discounts: prices one order. The status codes mirror the CLI's exit codes:
/// 200 = priced, 422 = the order breaks a discount rule (exit code 1), 400 = the order cannot be read (exit code 2).
/// </summary>
public static class DiscountEndpoint
{
    /// <summary>
    /// An order is a few kilobytes. Anything above this is refused before it is parsed, so one request
    /// cannot make the server read an unbounded body into memory.
    /// </summary>
    public const int MaxOrderBytes = 1_048_576;

    public static async Task<IResult> PriceAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.HasJsonContentType())
        {
            return Problem(StatusCodes.Status415UnsupportedMediaType, "Send the order as application/json", []);
        }

        if (OtherCharset(request) is { } charset)
        {
            return Problem(
                StatusCodes.Status415UnsupportedMediaType,
                "Send the order as UTF-8 JSON",
                [$"charset \"{charset}\" is not supported; use utf-8 or leave the charset out"]);
        }

        byte[]? body = await ReadBodyAsync(request, cancellationToken);
        if (body is null)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge, Invariant($"The order is larger than {MaxOrderBytes:N0} bytes"), []);
        }

        try
        {
            // Decodes strict UTF-8: bytes that are not UTF-8 are a 400, never U+FFFD in a priced order.
            OrderRequest order = OrderRequestParser.ParseUtf8(body);
            DiscountResult result = DiscountCalculator.Calculate(order.Cart, order.Campaigns);
            return Results.Json(PricedOrder.From(result), PricedOrder.JsonOptions);
        }
        catch (InputFormatException ex)
        {
            // Already capped by the parser (ProblemList.MaxReportedProblems).
            return Problem(StatusCodes.Status400BadRequest, "The order could not be read", ex.Errors);
        }
        catch (DiscountValidationException ex)
        {
            // Every broken rule is one message, so a large order could list thousands; show the first ones only.
            return Problem(StatusCodes.Status422UnprocessableEntity, "The order breaks a discount rule", ProblemList.ForDisplay(ex.Errors));
        }
    }

    /// <summary>
    /// The charset named in Content-Type when it is not UTF-8, otherwise null. JSON exchanged between systems
    /// is UTF-8 (RFC 8259), so no charset means UTF-8; any other one is refused rather than decoded wrongly.
    /// </summary>
    private static string? OtherCharset(HttpRequest request)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out MediaTypeHeaderValue? mediaType) || !mediaType.Charset.HasValue)
        {
            return null;
        }

        string charset = HeaderUtilities.RemoveQuotes(mediaType.Charset).ToString();
        return charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase) || charset.Equals("utf8", StringComparison.OrdinalIgnoreCase)
            ? null
            : charset;
    }

    /// <summary>
    /// Reads the body's bytes, or returns null once they pass <see cref="MaxOrderBytes"/>. The limit is
    /// checked while reading, because a request may not say its size up front (chunked transfer encoding).
    /// </summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxOrderBytes)
        {
            return null;
        }

        using var body = new MemoryStream();
        byte[] buffer = new byte[16_384];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (body.Length + read > MaxOrderBytes)
            {
                return null;
            }

            body.Write(buffer, 0, read);
        }

        return body.ToArray();
    }

    private static IResult Problem(int statusCode, string title, IReadOnlyList<string> errors)
        => Results.Problem(
            statusCode: statusCode,
            title: title,
            extensions: new Dictionary<string, object?> { ["errors"] = errors });
}

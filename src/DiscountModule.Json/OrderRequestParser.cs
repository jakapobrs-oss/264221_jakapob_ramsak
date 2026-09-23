using System.Text;
using System.Text.Json;
using DiscountModule.Campaigns;
using DiscountModule.Models;

namespace DiscountModule.Json;

/// <summary>An order read from JSON, ready to be handed to <see cref="DiscountCalculator"/>.</summary>
public sealed record OrderRequest(Cart Cart, IReadOnlyList<Campaign> Campaigns);

/// <summary>
/// Turns a JSON order into a cart and a campaign list, for both the CLI and the HTTP API.
/// It checks the shape of the document: JSON types, required and unexpected properties, known campaign types
/// and categories. It reports every shape problem at once, up to <see cref="ProblemList.MaxReportedProblems"/>;
/// past that it stops reading and says so. A JSON syntax error stops it at the first one.
/// Business rules such as a negative price or two coupons are left to <see cref="DiscountCalculator"/>,
/// which reports them with its own messages.
/// </summary>
public static class OrderRequestParser
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <exception cref="InputFormatException">The file cannot be read, or its content is not a valid order.</exception>
    public static OrderRequest ReadFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new InputFormatException($"Cannot read '{path}': {ex.Message}");
        }

        return Parse(json);
    }

    /// <exception cref="InputFormatException">The reader fails, or its content is not a valid order.</exception>
    public static OrderRequest Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        string json;
        try
        {
            json = reader.ReadToEnd();
        }
        catch (IOException ex)
        {
            throw new InputFormatException($"Cannot read standard input: {ex.Message}");
        }

        return Parse(json);
    }

    /// <exception cref="InputFormatException">The text is not a valid order.</exception>
    public static OrderRequest Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InputFormatException("Input is empty; expected a JSON object with \"items\" and \"campaigns\"");
        }

        using JsonDocument document = ParseDocument(json);
        List<string> errors = [];
        OrderRequest? request = ReadOrder(document.RootElement, errors);

        if (request is null || errors.Count > 0)
        {
            throw new InputFormatException(TooManyProblems(errors)
                ? [.. errors.Take(ProblemList.MaxReportedProblems), ProblemList.StoppedMessage]
                : errors);
        }

        return request;
    }

    /// <summary>
    /// True once the order has more problems than are reported. The readers below then stop, so a huge
    /// broken order costs a few messages instead of millions.
    /// </summary>
    private static bool TooManyProblems(List<string> errors) => errors.Count > ProblemList.MaxReportedProblems;

    private static JsonDocument ParseDocument(string json)
    {
        try
        {
            return JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            // The message ends with "LineNumber: 0 | BytePositionInLine: 2."; report line and column 1-based instead.
            string reason = ex.Message.Split(" LineNumber:", 2)[0].TrimEnd('.', ' ');
            string location = ex.LineNumber is { } line && ex.BytePositionInLine is { } bytePosition
                ? $" at line {line + 1}, column {ColumnOf(json, line, bytePosition)}"
                : "";
            throw new InputFormatException($"Malformed JSON{location}: {reason}");
        }
        catch (ArgumentException)
        {
            // Keep JsonDocument.Parse the only call in this try: here ArgumentException can only mean text that
            // cannot be encoded as UTF-8. Only reachable through Parse(string): text read from a file, stdin or an
            // HTTP body never holds half a surrogate pair.
            throw new InputFormatException("Input is not valid text: it contains half of a surrogate pair");
        }
    }

    /// <summary>
    /// Converts the reader's position, counted in UTF-8 bytes, into a 1-based character column.
    /// The two differ after non-ASCII text: each Thai letter is 3 bytes but 1 character.
    /// </summary>
    private static long ColumnOf(string json, long line, long bytePosition)
    {
        string lineText = json.Split('\n').ElementAtOrDefault((int)line) ?? "";
        byte[] lineBytes = Encoding.UTF8.GetBytes(lineText);
        int byteCount = (int)Math.Clamp(bytePosition, 0, lineBytes.Length);
        return Encoding.UTF8.GetCharCount(lineBytes, 0, byteCount) + 1;
    }

    private static OrderRequest? ReadOrder(JsonElement root, List<string> errors)
    {
        JsonObjectReader? order = JsonObjectReader.Open(root, "$", errors);
        if (order is null)
        {
            return null;
        }

        List<CartItem> items = order.Array("items") is { } itemArray ? ReadItems(itemArray, errors) : [];

        // Leaving "campaigns" out means no campaigns: the final price is then the subtotal.
        List<Campaign> campaigns = order.Array("campaigns", required: false) is { } campaignArray
            ? ReadCampaigns(campaignArray, errors)
            : [];

        order.RejectUnexpectedProperties();
        return new OrderRequest(new Cart(items), campaigns);
    }

    private static List<CartItem> ReadItems(JsonElement array, List<string> errors)
    {
        List<CartItem> items = [];
        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            if (TooManyProblems(errors))
            {
                break;
            }

            JsonObjectReader? item = JsonObjectReader.Open(element, $"$.items[{index++}]", errors);
            if (item is null)
            {
                continue;
            }

            string? name = item.String("name");
            ItemCategory? category = item.OneOf<ItemCategory>("category");
            decimal? unitPrice = item.Number("unitPrice");
            int? quantity = item.WholeNumber("quantity", required: false);
            item.RejectUnexpectedProperties();

            if (!item.HasErrors && name is not null && category is { } itemCategory && unitPrice is { } price)
            {
                items.Add(new CartItem(name, itemCategory, price, quantity ?? 1));
            }
        }

        return items;
    }

    private static List<Campaign> ReadCampaigns(JsonElement array, List<string> errors)
    {
        List<Campaign> campaigns = [];
        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            if (TooManyProblems(errors))
            {
                break;
            }

            JsonObjectReader? json = JsonObjectReader.Open(element, $"$.campaigns[{index++}]", errors);
            if (json is not null && CampaignTypes.Read(json) is { } campaign)
            {
                campaigns.Add(campaign);
            }
        }

        return campaigns;
    }
}

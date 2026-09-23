using System.Text.Json;

namespace DiscountModule.Cli.Input;

/// <summary>Reads the JSON input file (or standard input) into <see cref="OrderRequestDto"/>.</summary>
public static class OrderRequestReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <exception cref="InputFormatException">The file is missing, unreadable or not valid JSON for the expected shape.</exception>
    public static OrderRequestDto ReadFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new InputFormatException($"Cannot read '{path}': {ex.Message}");
        }

        return Parse(json);
    }

    /// <exception cref="InputFormatException">The text is not valid JSON for the expected shape.</exception>
    public static OrderRequestDto Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InputFormatException("Input is empty; expected a JSON object with \"items\" and \"campaigns\"");
        }

        try
        {
            return JsonSerializer.Deserialize<OrderRequestDto>(json, Options)
                ?? throw new InputFormatException("Input is 'null'; expected a JSON object with \"items\" and \"campaigns\"");
        }
        catch (JsonException ex)
        {
            throw new InputFormatException(Describe(ex));
        }
    }

    /// <summary>Turns a JsonException into a one-line message that points at the offending value.</summary>
    private static string Describe(JsonException ex)
    {
        var location = ex.Path is null ? "" : $" at {ex.Path}";
        var line = ex.LineNumber is null ? "" : $" (line {ex.LineNumber + 1})";

        // System.Text.Json messages are long and mention CLR type names; keep only the first sentence.
        var reason = ex.Message.Split(". ", 2)[0].TrimEnd('.');
        return $"Malformed JSON{location}{line}: {reason}";
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;

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
        return $"Malformed JSON{location}{line}: {Reason(ex.Message)}";
    }

    /// <summary>
    /// System.Text.Json messages are long and mention CLR type names. Translate the two common cases
    /// into plain words and otherwise keep only the first sentence.
    /// </summary>
    private static string Reason(string message)
    {
        var unknownProperty = UnknownPropertyPattern.Match(message);
        if (unknownProperty.Success)
        {
            return $"unknown property \"{unknownProperty.Groups["name"].Value}\"";
        }

        var wrongType = WrongTypePattern.Match(message);
        if (wrongType.Success)
        {
            return $"expected {FriendlyTypeName(wrongType.Groups["type"].Value)}";
        }

        return message.Split(". ", 2)[0].TrimEnd('.');
    }

    private static readonly Regex UnknownPropertyPattern =
        new("The JSON property '(?<name>[^']*)' could not be mapped", RegexOptions.CultureInvariant);

    private static readonly Regex WrongTypePattern =
        new(@"could not be converted to (?<type>\S+)", RegexOptions.CultureInvariant);

    private static string FriendlyTypeName(string clrType) => clrType switch
    {
        _ when clrType.Contains("Decimal", StringComparison.Ordinal) => "a number",
        _ when clrType.Contains("Int32", StringComparison.Ordinal) => "a whole number",
        _ when clrType.Contains("String", StringComparison.Ordinal) => "text in quotes",
        _ when clrType.Contains("List", StringComparison.Ordinal) => "an array",
        _ when clrType.Contains("Dto", StringComparison.Ordinal) => "an object",
        _ => "a different value type",
    };
}

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace DiscountModule.Json;

/// <summary>
/// Reads the properties of one JSON object and records every problem instead of stopping at the first.
/// Each problem starts with its JSON path, e.g. <c>$.items[0].unitPrice: expected a number, got the string "250"</c>.
/// Property names match in any letter case. These are all reported: a property given twice, a missing
/// required property, a value of the wrong JSON type (null included), and a property that no read asked for.
/// </summary>
internal sealed class JsonObjectReader
{
    /// <summary>
    /// JSON allows an escape for half of a surrogate pair on its own, but a .NET string cannot hold it:
    /// reading such a name or value throws. Found by the fuzz tests.
    /// </summary>
    private const string UnpairedSurrogate = "it contains an unpaired surrogate escape";

    private readonly Dictionary<string, JsonElement> properties = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> namesInDocumentOrder = [];
    private readonly List<string> expectedNames = [];
    private readonly List<string> errors;
    private readonly int errorCountAtStart;
    private readonly string path;

    private JsonObjectReader(JsonElement element, string path, List<string> errors)
    {
        this.path = path;
        this.errors = errors;
        errorCountAtStart = errors.Count;

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!TryReadText(() => property.Name, out string? propertyName))
            {
                errors.Add($"{path}: a property name is not valid text ({UnpairedSurrogate})");
                continue;
            }

            // Names match in any case, so "unitPrice" and "UNITPRICE" count as one property given twice.
            // Silently keeping one of the two values could hide a typo in a price.
            if (properties.TryAdd(propertyName, property.Value))
            {
                namesInDocumentOrder.Add(propertyName);
            }
            else
            {
                errors.Add($"{path}: property \"{propertyName}\" is given more than once");
            }
        }
    }

    /// <summary>True when a problem was recorded for this object, including its duplicate properties.</summary>
    public bool HasErrors => errors.Count > errorCountAtStart;

    /// <summary>Starts reading <paramref name="element"/>. Returns null and records a problem when it is not an object.</summary>
    public static JsonObjectReader? Open(JsonElement element, string path, List<string> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path}: expected an object, got {Describe(element)}");
            return null;
        }

        return new JsonObjectReader(element, path, errors);
    }

    /// <summary>Reads a required string.</summary>
    public string? String(string name)
    {
        if (!TryGet(name, required: true, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            Report(name, $"expected a string, got {Describe(value)}");
            return null;
        }

        if (!TryReadText(value.GetString, out string? text))
        {
            Report(name, $"{Describe(value)} is not valid text ({UnpairedSurrogate})");
            return null;
        }

        return text;
    }

    /// <summary>Reads a required number as a decimal.</summary>
    public decimal? Number(string name)
    {
        if (!TryGet(name, required: true, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number)
        {
            Report(name, $"expected a number, got {Describe(value)}");
            return null;
        }

        return TryConvert(name, value, out decimal number) ? number : null;
    }

    /// <summary>Reads a whole number. 3, 3.0 and 3e0 are accepted; 3.5 is not.</summary>
    public int? WholeNumber(string name, bool required = true)
    {
        if (!TryGet(name, required, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number)
        {
            Report(name, $"expected a whole number, got {Describe(value)}");
            return null;
        }

        if (!TryConvert(name, value, out decimal number))
        {
            return null;
        }

        if (number != decimal.Truncate(number))
        {
            Report(name, $"expected a whole number, got {value.GetRawText()}");
            return null;
        }

        if (number < int.MinValue || number > int.MaxValue)
        {
            Report(name, $"{value.GetRawText()} is out of range");
            return null;
        }

        return (int)number;
    }

    /// <summary>Reads a required string that must be one of <paramref name="allowed"/> (any letter case). Returns the allowed spelling.</summary>
    public string? OneOf(string name, IReadOnlyList<string> allowed)
    {
        string? text = String(name);
        if (text is null)
        {
            return null;
        }

        string? match = allowed.FirstOrDefault(candidate => string.Equals(candidate, text, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            Report(name, $"unknown value \"{text}\" (expected one of: {string.Join(", ", allowed)})");
        }

        return match;
    }

    /// <summary>
    /// Reads a required string that must be the name of a <typeparamref name="TEnum"/> member (any letter case).
    /// Only whole names match: <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> would also accept
    /// "1" or "Clothing,Accessories", which are not categories.
    /// </summary>
    public TEnum? OneOf<TEnum>(string name)
        where TEnum : struct, Enum
        => OneOf(name, Enum.GetNames<TEnum>()) is { } match ? Enum.Parse<TEnum>(match) : null;

    /// <summary>Reads an array. A missing optional array returns null without a problem.</summary>
    public JsonElement? Array(string name, bool required = true)
    {
        if (!TryGet(name, required, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            Report(name, $"expected an array, got {Describe(value)}");
            return null;
        }

        return value;
    }

    /// <summary>
    /// Reports every property that no read asked for, even one whose value is null.
    /// Call it after all reads, so the expected names are known.
    /// </summary>
    public void RejectUnexpectedProperties()
    {
        foreach (string name in namesInDocumentOrder)
        {
            if (!expectedNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"{path}: unexpected property \"{name}\" (expected: {string.Join(", ", expectedNames)})");
            }
        }
    }

    /// <summary>Describes a JSON value for a message, e.g. "an array", "null" or the string "250".</summary>
    private static string Describe(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => $"the string {element.GetRawText()}",
        JsonValueKind.Number => $"the number {element.GetRawText()}",
        _ => element.GetRawText(), // true, false or null
    };

    /// <summary>
    /// Converts a JSON number to <see cref="decimal"/>, or reports why that would change its value.
    /// <see cref="JsonElement.TryGetDecimal(out decimal)"/> rounds silently beyond the ~28 significant
    /// digits a decimal holds: 1e-30 would become 0 (a free item) and 100.000000000000000000000000001
    /// would become 100, hiding digits from the module's 2-decimal rules.
    /// </summary>
    private bool TryConvert(string name, JsonElement value, out decimal number)
    {
        string written = value.GetRawText();
        if (!value.TryGetDecimal(out number))
        {
            Report(name, $"{written} is out of range");
            return false;
        }

        string? writtenValue = CanonicalNumber(written);
        if (writtenValue != CanonicalNumber(number.ToString(CultureInfo.InvariantCulture)))
        {
            Report(name, number == 0 && writtenValue != "0"
                ? $"{written} is too small"
                : $"{written} has more digits than can be read exactly");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Writes a number as its sign, significant digits and exponent, so that equal values compare equal
    /// however they are written: "100.50", "1.005e2" and "1005E-1" all become "1005e-1"; any zero becomes "0".
    /// Returns null when the exponent is too large to read, which no decimal can match.
    /// </summary>
    private static string? CanonicalNumber(string text)
    {
        bool negative = text.StartsWith('-');
        string unsigned = negative ? text[1..] : text;

        int exponentStart = unsigned.IndexOfAny(['e', 'E']);
        string mantissa = exponentStart < 0 ? unsigned : unsigned[..exponentStart];
        int point = mantissa.IndexOf('.');
        string digits = point < 0 ? mantissa : mantissa.Remove(point, 1);

        string withoutLeadingZeros = digits.TrimStart('0');
        if (withoutLeadingZeros.Length == 0)
        {
            return "0";
        }

        long exponent = 0;
        if (exponentStart >= 0
            && !long.TryParse(unsigned[(exponentStart + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
        {
            return null;
        }

        string significant = withoutLeadingZeros.TrimEnd('0');
        exponent -= point < 0 ? 0 : mantissa.Length - point - 1;          // digits after the point
        exponent += withoutLeadingZeros.Length - significant.Length;     // trailing zeros removed
        return $"{(negative ? "-" : "")}{significant}e{exponent}";
    }

    /// <summary>Reads a name or a string value, or returns false when it holds an unpaired surrogate escape.</summary>
    private static bool TryReadText(Func<string?> read, [NotNullWhen(true)] out string? text)
    {
        try
        {
            text = read();
            return text is not null;
        }
        catch (InvalidOperationException)
        {
            text = null;
            return false;
        }
    }

    private bool TryGet(string name, bool required, out JsonElement value)
    {
        expectedNames.Add(name);
        if (properties.TryGetValue(name, out value))
        {
            return true;
        }

        if (required)
        {
            errors.Add($"{path}: \"{name}\" is required");
        }

        return false;
    }

    private void Report(string name, string problem) => errors.Add($"{path}.{name}: {problem}");
}

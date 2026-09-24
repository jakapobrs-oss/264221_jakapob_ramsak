using System.Globalization;
using System.Text;

namespace DiscountModule.Cli.Output;

/// <summary>
/// Makes text that came from the input safe to print on one terminal line. Control characters such as
/// a newline, a tab or ESC are written as escapes (\n, \t, \u001b) instead of breaking the line or
/// sending terminal commands, so every item and every error stays on its own line. Characters that change
/// the direction of the text are escaped the same way, so they cannot reorder how the line is shown, and
/// so are the line and paragraph separators, which many programs treat as line breaks.
/// </summary>
public static class ConsoleText
{
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Any(NeedsEscape))
        {
            return text;
        }

        var escaped = new StringBuilder(text.Length + 8);
        foreach (char character in text)
        {
            escaped.Append(character switch
            {
                '\n' => @"\n",
                '\r' => @"\r",
                '\t' => @"\t",
                _ when NeedsEscape(character) => @"\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture),
                _ => character.ToString(),
            });
        }

        return escaped.ToString();
    }

    private static bool NeedsEscape(char character)
        => IsLineOrParagraphSeparator(character) || char.IsControl(character) || IsBidirectionalControl(character);

    /// <summary>
    /// True for U+2028 LINE SEPARATOR and U+2029 PARAGRAPH SEPARATOR. They are not control characters, but
    /// editors, pagers and most programs that split text into lines start a new line at them, so a name
    /// holding one could put text that looks like a line of the breakdown on a line of its own.
    /// </summary>
    private static bool IsLineOrParagraphSeparator(char character)
        => char.GetUnicodeCategory(character) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    /// <summary>
    /// True for the characters that change the direction of the text after them: U+061C, U+200E, U+200F,
    /// U+202A to U+202E and U+2066 to U+2069. Printed as they are, they reorder how the rest of the line is
    /// shown, so an item name could make an amount read differently on screen ("Trojan Source",
    /// CVE-2021-42574). Other invisible characters are kept: the zero width space and the joiners
    /// (U+200B to U+200D) are part of real Thai, Arabic and emoji text.
    /// </summary>
    private static bool IsBidirectionalControl(char character)
        => (int)character is 0x061C or 0x200E or 0x200F or (>= 0x202A and <= 0x202E) or (>= 0x2066 and <= 0x2069);
}

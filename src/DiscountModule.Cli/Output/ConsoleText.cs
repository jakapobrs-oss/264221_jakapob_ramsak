using System.Globalization;
using System.Text;

namespace DiscountModule.Cli.Output;

/// <summary>
/// Makes text that came from the input safe to print on one terminal line. Control characters such as
/// a newline, a tab or ESC are written as escapes (\n, \t, \u001b) instead of breaking the line or
/// sending terminal commands, so every item and every error stays on its own line.
/// </summary>
public static class ConsoleText
{
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Any(char.IsControl))
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
                _ when char.IsControl(character) => @"\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture),
                _ => character.ToString(),
            });
        }

        return escaped.ToString();
    }
}

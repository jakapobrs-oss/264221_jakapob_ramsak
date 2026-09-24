using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiscountModule.Cli.Tests;

/// <summary>
/// Fuzz tests: thousands of damaged copies of the example orders, generated from a fixed seed so that every run
/// checks exactly the same inputs. Whatever the damage, the CLI must print either a result or an explanation,
/// exit with one of its documented codes, and never throw.
/// </summary>
public class CliFuzzTests
{
    private const int Seed = 20260923;
    private const int CasesPerFile = 300;

    private static readonly string ExampleDirectory = Path.Combine(AppContext.BaseDirectory, "examples");

    // JSON escapes are assembled at run time ("\\u" + ...) so that no tool or editor turns them into raw characters.
    private static readonly string LoneSurrogate = "\"\\u" + "D800\"";
    private static readonly string EscapeSequence = "\"\\u" + "001b[31m\"";
    private static readonly string LineSeparator = "\"a\\u" + "2028b\"";

    /// <summary>Fragments that tend to break parsers: structure characters, extreme numbers and unusual strings.</summary>
    private static readonly string[] Fragments =
    [
        "{", "}", "[", "]", ",", ":", "\"", "null", "true", "-1", "0", "-0", "0.005", "1e-30", "1e30", "1e400",
        "79228162514264337593543950336", "2147483648", "2.5", "\"\"", "\" \"", "\"Clothing,Accessories\"", "\"1\"",
        "\"เสื้อยืด\"", "\"😀\"", "\"line\\nbreak\"", EscapeSequence, LineSeparator, LoneSurrogate, "\"" + new string('x', 5_000) + "\"",
    ];

    private static readonly Regex Value = new(@"(?<=:\s*)(-?\d[\d.eE+-]*|""(?:[^""\\]|\\.)*"")", RegexOptions.CultureInvariant);

    public static TheoryData<string> ExampleFiles
    {
        get
        {
            var files = new TheoryData<string>();
            foreach (string path in Directory.GetFiles(ExampleDirectory, "*.json").Order(StringComparer.Ordinal))
            {
                files.Add(Path.GetFileName(path));
            }

            return files;
        }
    }

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public void Damaged_orders_never_crash_the_cli(string file)
    {
        string original = File.ReadAllText(Path.Combine(ExampleDirectory, file));

        // A stable seed per file: string.GetHashCode changes from one process to the next, a character sum does not.
        int seed = Seed + file.Sum(character => character);
        var random = new Random(seed);

        for (int caseNumber = 0; caseNumber < CasesPerFile; caseNumber++)
        {
            string input = Damage(original, random);
            bool asJson = caseNumber % 2 == 1;
            string[] args = asJson ? ["-", "--json"] : ["-"];
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            string context = $"{file}, case {caseNumber} (seed {seed}), input: {Shorten(input)}";

            int exitCode;
            try
            {
                exitCode = CliApp.Run(args, stdout, stderr, new StringReader(input));
            }
            catch (Exception ex)
            {
                Assert.Fail($"{context} threw {ex.GetType().Name}: {ex.Message}");
                return;
            }

            Assert.True(
                exitCode is CliApp.ExitSuccess or CliApp.ExitValidationError or CliApp.ExitInputError,
                $"{context} exited with {exitCode}");

            string output = stdout.ToString();
            string errors = stderr.ToString();
            if (exitCode == CliApp.ExitSuccess)
            {
                Assert.True(output.Length > 0 && errors.Length == 0, $"{context}: success must print a result and no error");
                if (asJson)
                {
                    using JsonDocument _ = JsonDocument.Parse(output); // the result is valid JSON
                }
            }
            else
            {
                Assert.True(output.Length == 0 && errors.Length > 0, $"{context}: a failure must explain itself on stderr only");
            }

            // Whatever the input, nothing printed carries control characters other than line breaks, nor the
            // line and paragraph separators that editors and pagers also treat as line breaks.
            Assert.False(
                (output + errors).Any(character => char.IsControl(character) && character is not ('\r' or '\n')),
                $"{context}: printed a control character");
            Assert.False(
                (output + errors).Any(character => character == (char)0x2028 || character == (char)0x2029),
                $"{context}: printed a line or paragraph separator");
        }
    }

    /// <summary>Applies 1 to 3 random kinds of damage: delete, insert, replace a value, cut short, repeat a slice.</summary>
    private static string Damage(string json, Random random)
    {
        var text = new StringBuilder(json);
        int steps = random.Next(1, 4);
        for (int step = 0; step < steps; step++)
        {
            int position = random.Next(text.Length + 1);
            switch (random.Next(5))
            {
                case 0:
                    text.Remove(position, Math.Min(random.Next(1, 6), text.Length - position));
                    break;
                case 1:
                    text.Insert(position, Fragments[random.Next(Fragments.Length)]);
                    break;
                case 2:
                    MatchCollection values = Value.Matches(text.ToString());
                    if (values.Count > 0)
                    {
                        Match value = values[random.Next(values.Count)];
                        text.Remove(value.Index, value.Length).Insert(value.Index, Fragments[random.Next(Fragments.Length)]);
                    }

                    break;
                case 3:
                    text.Length = position;
                    break;
                default:
                    int start = random.Next(text.Length + 1);
                    int length = Math.Min(random.Next(1, 40), text.Length - start);
                    text.Insert(position, text.ToString(start, length));
                    break;
            }
        }

        return text.ToString();
    }

    private static string Shorten(string input) => input.Length <= 300 ? input : input[..300] + "…";
}

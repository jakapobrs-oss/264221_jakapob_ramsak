using System.Text;
using System.Text.Json;

using DiscountModule.Json;

namespace DiscountModule.Cli.Tests;

/// <summary>The CLI reads an order as strict UTF-8, from a file and from standard input.</summary>
public class Utf8Tests
{
    private const string BeforeName = "{ \"items\": [ { \"name\": \"";

    private static readonly byte[] OrderWithByteFF =
        [.. Encoding.UTF8.GetBytes(BeforeName), 0xFF, .. Encoding.UTF8.GetBytes("\", \"category\": \"Clothing\", \"unitPrice\": 350 } ] }")];

    private static string Example(string name) => Path.Combine(AppContext.BaseDirectory, "examples", name);

    private static string[] Lines(string output) => output.TrimEnd().Split(Environment.NewLine);

    private static (int ExitCode, string Stdout, string Stderr) Run(TextReader? stdin, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int exitCode = CliApp.Run(args, stdout, stderr, stdin);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private static (int ExitCode, string Stdout, string Stderr) RunWithFile(byte[] content, params string[] options)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        File.WriteAllBytes(path, content);
        try
        {
            return Run(stdin: null, [.. options, path]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_order_file_with_a_byte_that_is_not_utf8_exits_with_2_and_gives_its_offset()
    {
        var (exitCode, stdout, stderr) = RunWithFile(OrderWithByteFF);

        Assert.Equal(CliApp.ExitInputError, exitCode);
        Assert.Equal("", stdout);
        Assert.Equal(["Input error (1):", $"  - Input is not valid UTF-8 text (byte 0xFF at offset {BeforeName.Length})"], Lines(stderr));
    }

    [Fact]
    public void A_utf16_order_file_exits_with_2_and_says_it_must_be_utf8()
    {
        // What "Get-Content order.json > copy.json" writes in Windows PowerShell 5.1.
        string order = File.ReadAllText(Example("01-fixed-amount.json"));
        var (exitCode, _, stderr) = RunWithFile([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(order)]);

        Assert.Equal(CliApp.ExitInputError, exitCode);
        Assert.Equal(
            ["Input error (1):", "  - Input is UTF-16 text (it starts with a UTF-16 byte order mark); it must be UTF-8"],
            Lines(stderr));
    }

    [Fact]
    public void A_utf8_order_file_with_a_byte_order_mark_is_priced()
    {
        byte[] order = File.ReadAllBytes(Example("01-fixed-amount.json"));
        var (exitCode, stdout, _) = RunWithFile([.. Encoding.UTF8.GetPreamble(), .. order], "--json");

        Assert.Equal(CliApp.ExitSuccess, exitCode);
        using JsonDocument result = JsonDocument.Parse(stdout);
        Assert.Equal(550.00m, result.RootElement.GetProperty("finalPrice").GetDecimal());
    }

    [Fact]
    public void Standard_input_with_a_byte_that_is_not_utf8_exits_with_2()
    {
        // Program.cs reads standard input through this reader.
        using TextReader stdin = OrderRequestParser.CreateUtf8Reader(new MemoryStream(OrderWithByteFF));

        var (exitCode, stdout, stderr) = Run(stdin, "-");

        Assert.Equal(CliApp.ExitInputError, exitCode);
        Assert.Equal("", stdout);
        Assert.Equal(["Input error (1):", "  - Input is not valid UTF-8 text (byte 0xFF)"], Lines(stderr));
    }
}

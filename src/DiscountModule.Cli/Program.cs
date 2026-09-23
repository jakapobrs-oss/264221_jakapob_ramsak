using System.Text;

using DiscountModule.Cli;
using DiscountModule.Json;

// Item names may be Thai or any other script. On an old console code page they would print as "?", and
// output redirected to a file would not be UTF-8, so this run switches the console to UTF-8. The code page
// belongs to the whole console window, so the original one is put back before the program exits.
Encoding originalOutput = Console.OutputEncoding;
Encoding originalInput = Console.InputEncoding;
SetConsoleEncodings(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

try
{
    // Standard input is read as bytes and decoded as strict UTF-8. Console.In would silently turn bytes that
    // are not UTF-8 into U+FFFD, and the order would be priced from text its sender never wrote.
    using TextReader stdin = OrderRequestParser.CreateUtf8Reader(Console.OpenStandardInput());
    return CliApp.RunSafely(args, Console.Out, Console.Error, stdin);
}
finally
{
    SetConsoleEncodings(originalOutput, originalInput);
}

static void SetConsoleEncodings(Encoding output, Encoding input)
{
    try
    {
        Console.OutputEncoding = output;
        Console.InputEncoding = input;
    }
    catch (IOException)
    {
        // No console attached (e.g. started by a service); keep whatever is in place.
    }
}

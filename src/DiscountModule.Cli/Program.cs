using System.Text;

using DiscountModule.Cli;

// Item names may be Thai or any other script. On an old console code page they would print as "?", and
// output redirected to a file would not be UTF-8, so this run switches the console to UTF-8. The code page
// belongs to the whole console window, so the original one is put back before the program exits.
Encoding originalOutput = Console.OutputEncoding;
Encoding originalInput = Console.InputEncoding;
SetConsoleEncodings(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

try
{
    return CliApp.RunSafely(args, Console.Out, Console.Error, Console.In);
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

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
    return CliApp.Run(args, Console.Out, Console.Error, Console.In);
}
catch (Exception ex)
{
    // CliApp turns every expected failure into a message and an exit code. Reaching this point means a bug
    // or a broken environment (e.g. the output pipe was closed), so report it briefly instead of a stack trace.
    Console.Error.WriteLine($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
    return CliApp.ExitUnexpectedError;
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

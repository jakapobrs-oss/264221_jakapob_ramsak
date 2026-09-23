using System.Text;
using DiscountModule.Cli;

UseUtf8Console();

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

// Item names may be Thai (or any other script). Without this, a console on an old code page prints "?"
// and output redirected to a file is not UTF-8.
static void UseUtf8Console()
{
    var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    try
    {
        Console.OutputEncoding = utf8;
        Console.InputEncoding = utf8;
    }
    catch (IOException)
    {
        // No console attached (e.g. started by a service); the default encoding stays in place.
    }
}

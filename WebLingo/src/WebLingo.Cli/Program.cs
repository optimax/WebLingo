namespace WebLingo.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] is "--version" or "version")
        {
            Console.WriteLine("WebLingo M0.6.1 scaffold (pre-alpha)");
            return 0;
        }
        if (args.Length == 2 && args[0] is "run" or "check")
        {
            if (!File.Exists(args[1]))
            {
                Console.Error.WriteLine($"File not found: {args[1]}");
                return 2;
            }
            Console.Error.WriteLine("WS0000: Compiler/VM execution is not implemented in M0.6.1.");
            return 3;
        }
        Console.WriteLine("Usage: weblingo --version | run <file.ws> | check <file.ws>");
        Console.WriteLine("Note: run/check are reserved commands; implementation begins in M0.6.2+.");
        return args.Length == 0 ? 0 : 2;
    }
}

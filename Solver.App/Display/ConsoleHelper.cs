using System;

namespace Solver.App.Display;

public static class ConsoleHelper
{
    public static void Header(string text)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 58));
        Console.WriteLine($" {text}");
        Console.WriteLine(new string('=', 58));
    }

    public static void Error(string text)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  {text}");
        Console.ForegroundColor = previous;
    }

    public static void Success(string text)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  {text}");
        Console.ForegroundColor = previous;
    }

    public static string Prompt(string label)
    {
        Console.Write($"{label}: ");
        return Console.ReadLine()?.Trim() ?? string.Empty;
    }

    public static void Pause()
    {
        Console.WriteLine();
        Console.Write("Press Enter to continue...");
        Console.ReadLine();
    }
}

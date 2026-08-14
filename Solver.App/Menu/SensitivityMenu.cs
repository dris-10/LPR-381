using System;
using Solver.App.Display;
using Solver.Core.Models;

namespace Solver.App.Menu;

/// <summary>OWNER: Person 1 wires the menu, Person 4 supplies the analyser.</summary>
public sealed class SensitivityMenu
{
    public void Run(LPModel model)
    {
        ConsoleHelper.Header("Sensitivity analysis");
        Console.WriteLine("  1. Range of a non-basic variable");
        Console.WriteLine("  2. Range of a basic variable");
        Console.WriteLine("  3. Range of a constraint RHS");
        Console.WriteLine("  4. Shadow prices");
        Console.WriteLine("  5. Add an activity");
        Console.WriteLine("  6. Add a constraint");
        Console.WriteLine("  7. Solve the dual / duality check");
        Console.WriteLine("  0. Back");
        Console.WriteLine();

        ConsoleHelper.Prompt("Choice");
        ConsoleHelper.Error("Sensitivity analysis is not built yet (Person 4).");
        ConsoleHelper.Pause();
    }
}

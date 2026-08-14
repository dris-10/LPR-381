using System;
using System.Collections.Generic;
using System.IO;
using Solver.Algorithms;
using Solver.App.Display;
using Solver.Core.IO;
using Solver.Core.Interfaces;
using Solver.Core.Models;

namespace Solver.App.Menu;

public sealed class AlgorithmMenu
{
    /// <summary>The only place concrete algorithm types are named. Everything else uses ISolver.</summary>
    private static readonly List<ISolver> Solvers = new()
    {
        new PrimalSimplex(),
        new RevisedPrimalSimplex(),
        new BranchAndBoundSimplex(),
        new KnapsackBranchAndBound(),
        new CuttingPlane()
    };

    public void Run(LPModel model, string inputPath)
    {
        ConsoleHelper.Header("Choose an algorithm");

        for (int i = 0; i < Solvers.Count; i++)
        {
            bool ok = Solvers[i].CanSolve(model);
            Console.WriteLine($"  {i + 1}. {Solvers[i].AlgorithmName}{(ok ? "" : "   [not valid for this model]")}");
        }
        Console.WriteLine("  0. Back");
        Console.WriteLine();

        if (!int.TryParse(ConsoleHelper.Prompt("Choice"), out int choice) || choice < 1 || choice > Solvers.Count)
            return;

        var solver = Solvers[choice - 1];

        if (!solver.CanSolve(model))
        {
            ConsoleHelper.Error($"{solver.AlgorithmName} cannot handle this model's structure.");
            ConsoleHelper.Pause();
            return;
        }

        try
        {
            var result = solver.Solve(model);
            TableauFormatter.PrintAllIterations(result);

            Console.WriteLine();
            Console.WriteLine(result.FormattedSolution(model.VariableNames));

            string outPath = Path.Combine(
                Path.GetDirectoryName(inputPath) ?? ".",
                Path.GetFileNameWithoutExtension(inputPath) + "_output.txt");

            OutputFileWriter.Write(outPath, model, result);
            ConsoleHelper.Success($"Output written to {outPath}");
        }
        catch (NotImplementedException ex)
        {
            ConsoleHelper.Error($"Not built yet: {ex.Message}");
        }
        catch (Exception ex)
        {
            ConsoleHelper.Error($"Solver failed: {ex.Message}");
        }

        ConsoleHelper.Pause();
    }
}

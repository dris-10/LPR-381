using System;
using System.IO;
using Solver.App.Display;
using Solver.Core.IO;
using Solver.Core.Models;

namespace Solver.App.Menu;

public sealed class MainMenu
{
    private LPModel? _model;
    private string _inputPath = string.Empty;

    public void Run(string[] args)
    {
        if (args.Length > 0) TryLoad(args[0]);

        while (true)
        {
            ConsoleHelper.Header("LPR381 - Linear and Integer Programming Solver");
            Console.WriteLine($"  Loaded model : {(_model is null ? "(none)" : Path.GetFileName(_inputPath))}");
            Console.WriteLine();
            Console.WriteLine("  1. Load input file");
            Console.WriteLine("  2. Show current model");
            Console.WriteLine("  3. Solve");
            Console.WriteLine("  4. Sensitivity analysis");
            Console.WriteLine("  0. Exit");
            Console.WriteLine();

            switch (ConsoleHelper.Prompt("Choice"))
            {
                case "1":
                    TryLoad(ConsoleHelper.Prompt("Path to input file"));
                    break;
                case "2":
                    if (Require()) { Console.WriteLine(); Console.WriteLine(_model); ConsoleHelper.Pause(); }
                    break;
                case "3":
                    if (Require()) new AlgorithmMenu().Run(_model!, _inputPath);
                    break;
                case "4":
                    if (Require()) new SensitivityMenu().Run(_model!);
                    break;
                case "0":
                    return;
                default:
                    ConsoleHelper.Error("Unknown option.");
                    break;
            }
        }
    }

    private bool Require()
    {
        if (_model is not null) return true;
        ConsoleHelper.Error("Load an input file first (option 1).");
        ConsoleHelper.Pause();
        return false;
    }

    private void TryLoad(string path)
    {
        try
        {
            _model = InputFileParser.ParseFile(path);
            _inputPath = path;
            ConsoleHelper.Success($"Loaded {_model.VariableCount} variables and {_model.ConstraintCount} constraints.");
        }
        catch (Exception ex)
        {
            ConsoleHelper.Error($"Could not load '{path}': {ex.Message}");
        }
        ConsoleHelper.Pause();
    }
}

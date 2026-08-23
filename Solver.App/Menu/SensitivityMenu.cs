using System;
using System.Globalization;
using System.Linq;
using Solver.Algorithms;
using Solver.App.Display;
using Solver.Core.Interfaces;
using Solver.Core.Models;
using Solver.Core.Results;
using Solver.Sensitivity;

namespace Solver.App.Menu;

/// <summary>
/// OWNER: Person 1 wires the menu, Person 4 supplies the analyser.
///
/// Every question on this menu is answered from an OPTIMAL tableau, so the model is solved
/// once on entry and that baseline result is reused for the whole session. The "change and
/// re-solve" options never touch the baseline - each one is an independent what-if.
/// </summary>
public sealed class SensitivityMenu
{
    private readonly ISensitivityAnalyzer _analyzer = new SensitivityAnalyzer();

    public void Run(LPModel model)
    {
        var baseline = SolveBaseline(model);
        if (baseline is null) return;

        while (true)
        {
            ConsoleHelper.Header("Sensitivity analysis");
            Console.WriteLine($"  Baseline ({baseline.AlgorithmName}): {baseline.FormattedSolution(model.VariableNames)}");
            Console.WriteLine();
            Console.WriteLine("  1. Range of a non-basic variable");
            Console.WriteLine("  2. Range of a basic variable");
            Console.WriteLine("  3. Range of a constraint RHS");
            Console.WriteLine("  4. Shadow prices");
            Console.WriteLine("  5. Change an objective coefficient and re-solve");
            Console.WriteLine("  6. Change a constraint RHS and re-solve");
            Console.WriteLine("  7. Add an activity");
            Console.WriteLine("  8. Add a constraint");
            Console.WriteLine("  9. Solve the dual / duality check");
            Console.WriteLine("  0. Back");
            Console.WriteLine();

            string choice = ConsoleHelper.Prompt("Choice");
            if (choice == "0") return;

            Console.WriteLine();
            try
            {
                Dispatch(choice, model, baseline);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException
                                        or InvalidOperationException or FormatException
                                        or NotSupportedException or NotImplementedException)
            {
                ConsoleHelper.Error(ex.Message);
            }

            ConsoleHelper.Pause();
        }
    }

    private void Dispatch(string choice, LPModel model, SolutionResult baseline)
    {
        switch (choice)
        {
            case "1":
                ShowRange("Non-basic range",
                    _analyzer.NonBasicVariableRange(baseline, PromptVariable(model)));
                break;

            case "2":
                ShowRange("Basic range",
                    _analyzer.BasicVariableRange(baseline, PromptVariable(model)));
                break;

            case "3":
                ShowRange("RHS range",
                    _analyzer.RhsRange(baseline, PromptConstraint(model)));
                break;

            case "4":
                ShowShadowPrices(model, _analyzer.ShadowPrices(baseline));
                break;

            case "5":
            {
                int index = PromptVariable(model);
                double value = PromptNumber($"New coefficient for {Label(model, index)}");
                PrintResult(_analyzer.ApplyObjectiveChange(baseline, index, value));
                break;
            }

            case "6":
            {
                int index = PromptConstraint(model);
                double value = PromptNumber($"New RHS for constraint {index + 1}");
                PrintResult(_analyzer.ApplyRhsChange(baseline, index, value));
                break;
            }

            case "7":
            {
                double objective = PromptNumber("Objective coefficient for the new activity");
                var column = PromptVector(
                    $"Its coefficient in each of the {model.ConstraintCount} constraints",
                    model.ConstraintCount);
                PrintResult(_analyzer.AddActivity(baseline, objective, column));
                break;
            }

            case "8":
            {
                var coefficients = PromptVector(
                    $"Coefficient for each of the {model.VariableCount} variables",
                    model.VariableCount);
                var constraint = new Constraint
                {
                    Coefficients = coefficients,
                    Relation = PromptRelation(),
                    Rhs = PromptNumber("RHS"),
                    Name = $"c{model.ConstraintCount + 1}"
                };
                PrintResult(_analyzer.AddConstraint(baseline, constraint));
                break;
            }

            case "9":
                ShowDuality(model, baseline);
                break;

            default:
                ConsoleHelper.Error("Unknown option.");
                break;
        }
    }

    // ---------- baseline ----------

    /// <summary>
    /// Sensitivity is read off an optimal tableau, so the model has to be solved first.
    /// Primal Simplex is used because it is the one algorithm guaranteed to populate
    /// FinalTableau today; an integer model is therefore analysed as its LP relaxation.
    /// </summary>
    private static SolutionResult? SolveBaseline(LPModel model)
    {
        ConsoleHelper.Header("Sensitivity analysis");
        Console.WriteLine("  Solving the model to get a baseline optimal tableau...");

        SolutionResult result;
        try
        {
            result = new PrimalSimplex().Solve(model);
        }
        catch (Exception ex)
        {
            ConsoleHelper.Error($"Could not solve the model: {ex.Message}");
            ConsoleHelper.Pause();
            return null;
        }

        if (!result.IsOptimal)
        {
            ConsoleHelper.Error($"The model is {result.Status} - there is no optimal tableau to analyse. {result.Message}");
            ConsoleHelper.Pause();
            return null;
        }

        if (result.FinalTableau is null || result.SourceModel is null)
        {
            ConsoleHelper.Error("The solver did not populate FinalTableau/SourceModel, which sensitivity analysis needs.");
            ConsoleHelper.Pause();
            return null;
        }

        ConsoleHelper.Success("Baseline ready.");
        if (model.HasIntegerRestrictions)
            ConsoleHelper.Error("Note: this model has int/bin variables. Sensitivity is reported for the LP relaxation.");

        ConsoleHelper.Pause();
        return result;
    }

    // ---------- output ----------

    private static void ShowRange(string label, (double Lower, double Upper) range)
        => Console.WriteLine($"  {label}: [{Bound(range.Lower)}, {Bound(range.Upper)}]");

    private static void ShowShadowPrices(LPModel model, double[] prices)
    {
        Console.WriteLine("  Shadow prices:");
        for (int i = 0; i < prices.Length; i++)
        {
            string name = i < model.ConstraintCount && !string.IsNullOrWhiteSpace(model.Constraints[i].Name)
                ? model.Constraints[i].Name
                : $"c{i + 1}";
            Console.WriteLine($"    {name,-8} {Math.Round(prices[i], 3),10:0.###}");
        }
    }

    private void ShowDuality(LPModel model, SolutionResult baseline)
    {
        var dual = _analyzer.BuildDual(model);
        Console.WriteLine("  Dual model:");
        Console.WriteLine(dual);

        var (dualResult, verdict) = _analyzer.AnalyseDuality(model, baseline);
        Console.WriteLine("  Dual solution:");
        PrintResult(dualResult);
        Console.WriteLine();
        Console.WriteLine($"  {verdict}");
    }

    private static void PrintResult(SolutionResult result)
    {
        // After AddActivity the model has one more variable than the names we started with,
        // so only pass names through when they still line up with the values.
        var names = result.SourceModel?.VariableNames;
        if (names is null || names.Length != result.VariableValues.Length) names = null;

        if (result.IsOptimal)
        {
            ConsoleHelper.Success(result.FormattedSolution(names));
            if (!string.IsNullOrWhiteSpace(result.Message))
                Console.WriteLine($"  {result.Message}");
        }
        else
        {
            ConsoleHelper.Error($"{result.Status}: {result.Message}");
        }
    }

    private static string Bound(double value)
        => double.IsPositiveInfinity(value) ? "+inf"
         : double.IsNegativeInfinity(value) ? "-inf"
         : $"{Math.Round(value, 3):0.###}";

    // ---------- input ----------

    private static string Label(LPModel model, int index)
        => index < model.VariableNames.Length && !string.IsNullOrWhiteSpace(model.VariableNames[index])
            ? model.VariableNames[index]
            : $"x{index + 1}";

    private static int PromptVariable(LPModel model)
        => PromptIndex($"Variable number (1-{model.VariableCount})", model.VariableCount);

    private static int PromptConstraint(LPModel model)
        => PromptIndex($"Constraint number (1-{model.ConstraintCount})", model.ConstraintCount);

    /// <summary>Prompts for a 1-based position and hands back the 0-based index.</summary>
    private static int PromptIndex(string label, int count)
    {
        string raw = ConsoleHelper.Prompt(label);
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            || value < 1 || value > count)
            throw new FormatException($"'{raw}' is not a number between 1 and {count}.");
        return value - 1;
    }

    private static double PromptNumber(string label)
    {
        string raw = ConsoleHelper.Prompt(label);
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new FormatException($"'{raw}' is not a number.");
        return value;
    }

    private static double[] PromptVector(string label, int expected)
    {
        string raw = ConsoleHelper.Prompt($"{label}, space separated");
        var tokens = raw.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length != expected)
            throw new FormatException($"Expected {expected} values, got {tokens.Length}.");

        return tokens.Select(t =>
            double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                ? v
                : throw new FormatException($"'{t}' is not a number.")).ToArray();
    }

    private static RelationType PromptRelation()
        => ConsoleHelper.Prompt("Relation (<=, >=, =)") switch
        {
            "<=" => RelationType.LessEqual,
            ">=" => RelationType.GreaterEqual,
            "=" => RelationType.Equal,
            var other => throw new FormatException($"'{other}' is not one of <=, >=, =.")
        };
}

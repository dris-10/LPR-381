using System;
using System.Linq;
using Solver.Core.Models;

using Solver.Core;

namespace Solver.Core.Results;

/// <summary>
/// FROZEN CONTRACT. What every ISolver hands back.
///
/// IMPORTANT: FinalTableau and SourceModel are NOT optional extras. Sensitivity analysis
/// (shadow prices, RHS ranging, ranging of basic/non-basic variables, the dual) is computed
/// FROM the optimal tableau. If a solver returns only ObjectiveValue and VariableValues,
/// Person 4 cannot do their half of the project. Always populate them on an Optimal result.
/// </summary>
public sealed class SolutionResult
{
    public required SolutionStatus Status { get; init; }
    public required string AlgorithmName { get; init; }

    /// <summary>Objective value in terms of the ORIGINAL model (Min problems already flipped back).</summary>
    public double ObjectiveValue { get; init; }

    /// <summary>Values of the original decision variables, in input-file order.</summary>
    public double[] VariableValues { get; init; } = Array.Empty<double>();

    /// <summary>Every tableau / node visited, in order.</summary>
    public IterationLog Log { get; init; } = new();

    /// <summary>The optimal tableau. Required for sensitivity analysis. Null if not Optimal.</summary>
    public Tableau? FinalTableau { get; init; }

    /// <summary>The model that was actually solved (post-branching for B&amp;B). Required for sensitivity.</summary>
    public LPModel? SourceModel { get; init; }

    /// <summary>Human-readable explanation, e.g. "Phase 1 ended with a positive artificial sum".</summary>
    public string Message { get; init; } = string.Empty;

    public bool IsOptimal => Status == SolutionStatus.Optimal;

    /// <summary>All output in this project is rounded to 3 decimals.</summary>
    public string FormattedSolution(string[]? names = null)
    {
        if (!IsOptimal) return $"{Status}: {Message}";
        names ??= Enumerable.Range(1, VariableValues.Length).Select(i => $"x{i}").ToArray();
        var vars = string.Join("  ", VariableValues.Select((v, i) => $"{names[i]}={Math.Round(v, 3):0.###}"));
        return $"z = {Math.Round(ObjectiveValue, 3):0.###}   {vars}";
    }

    public static SolutionResult Failure(SolutionStatus status, string algorithm, string message, IterationLog? log = null)
        => new()
        {
            Status = status,
            AlgorithmName = algorithm,
            Message = message,
            Log = log ?? new IterationLog()
        };
}

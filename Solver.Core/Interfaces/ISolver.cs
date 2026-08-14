using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Core.Interfaces;

/// <summary>
/// FROZEN CONTRACT - Day 1. Every algorithm implements exactly this.
/// The menu discovers solvers through this interface and never references a concrete class.
/// </summary>
public interface ISolver
{
    /// <summary>Menu label, e.g. "Branch and Bound (Simplex)".</summary>
    string AlgorithmName { get; }

    /// <summary>
    /// Cheap structural check. Knapsack B&amp;B returns false unless the model is a single
    /// &lt;= constraint with all-binary variables. The menu greys out anything that returns false.
    /// </summary>
    bool CanSolve(LPModel model);

    /// <summary>
    /// Solve. MUST NOT mutate the model it is given - clone first.
    /// MUST populate Log with the canonical form and every iteration.
    /// MUST populate FinalTableau and SourceModel when Status is Optimal.
    /// </summary>
    SolutionResult Solve(LPModel model);
}

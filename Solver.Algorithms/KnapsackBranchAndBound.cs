using System;
using System.Linq;
using Solver.Core.Interfaces;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Algorithms;

/// <summary>
/// OWNER: Person 3.  STATUS: not implemented yet.
/// Person 2 created this stub only so the solution compiles.
///
/// Rank items by value/weight, take them greedily, allow the last one to be split
/// for the bound, then branch on that fractional item.
/// </summary>
public sealed class KnapsackBranchAndBound : ISolver
{
    public string AlgorithmName => "Branch and Bound (Knapsack)";

    /// <summary>Only valid for a single &lt;= constraint with every variable binary.</summary>
    public bool CanSolve(LPModel model) =>
        model.IsPureBinary &&
        model.ConstraintCount == 1 &&
        model.Constraints[0].Relation == RelationType.LessEqual;

    public SolutionResult Solve(LPModel model)
        => throw new NotImplementedException("Person 3: implement Knapsack Branch and Bound.");
}

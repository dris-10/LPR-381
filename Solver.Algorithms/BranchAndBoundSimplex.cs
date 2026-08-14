using System;
using Solver.Core.Interfaces;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Algorithms;

/// <summary>
/// OWNER: Person 2.  STATUS: not implemented yet.
///
/// Branch and Bound over LP relaxations solved by PrimalSimplex.
///
/// Driver:
///   1. root = BranchNode.Root(model); solve its relaxation with PrimalSimplex
///   2. if infeasible -> whole problem infeasible
///   3. if every integer-restricted variable is already integral -> that is the answer
///   4. otherwise push the root and loop:
///        pop a node, solve its relaxation
///        prune if Infeasible
///        prune if the bound cannot beat the incumbent
///            (Max: relaxation z <= incumbent z, Min: relaxation z >= incumbent z)
///        if integral -> update the incumbent, prune
///        else pick a fractional integer variable and branch:
///            child A: x_i <= floor(value)
///            child B: x_i >= ceil(value)
///   5. return the incumbent
///
/// Log every node - the label, its branching constraint, its relaxation tableaux and
/// the reason it closed. Use IterationLog.Merge(nodeResult.Log, $"Node {node.Label}")
/// and IterationLog.Note for prune reasons. The marks are in the visible tree.
///
/// Branching variable rule: document whichever you pick (lowest index, or most fractional)
/// and stay consistent so hand-checked answers match.
/// Use Tableau.Epsilon when testing whether a value is integral - never ==.
/// </summary>
public sealed class BranchAndBoundSimplex : ISolver
{
    public string AlgorithmName => "Branch and Bound (Simplex)";

    public bool CanSolve(LPModel model) => model.HasIntegerRestrictions;

    public SolutionResult Solve(LPModel model)
        => throw new NotImplementedException("Person 2: implement Branch and Bound (Simplex).");
}

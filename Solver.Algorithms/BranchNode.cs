using System.Collections.Generic;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Algorithms;

/// <summary>
/// OWNER: Person 2. One node of the Branch and Bound tree.
///
/// Each node owns its OWN cloned model (parent model + one extra branching constraint),
/// so siblings can never corrupt each other.
/// </summary>
public sealed class BranchNode
{
    /// <summary>Display label: "0" for the root, then "1.1", "1.2", "1.1.1" and so on.</summary>
    public required string Label { get; init; }

    /// <summary>The LP relaxation solved at this node, including all inherited branching cuts.</summary>
    public required LPModel Model { get; init; }

    public BranchNode? Parent { get; init; }
    public int Depth => Parent is null ? 0 : Parent.Depth + 1;

    /// <summary>Human-readable branching constraint that created this node, e.g. "x2 &lt;= 3".</summary>
    public string BranchDescription { get; init; } = "root";

    /// <summary>Result of solving this node's relaxation. Set by the driver.</summary>
    public SolutionResult? Relaxation { get; set; }

    /// <summary>Why this node stopped: solved integral, pruned by bound, or infeasible.</summary>
    public string Outcome { get; set; } = "unexplored";

    public List<BranchNode> Children { get; } = new();

    /// <summary>
    /// Creates the root node from the original model. Labelled "1" so the driver's
    /// dotted child labels read "1.1", "1.2", "1.1.1" ... with the root itself as "Table 1".
    /// </summary>
    public static BranchNode Root(LPModel model) => new()
    {
        Label = "1",
        Model = model.Clone(),
        BranchDescription = "root"
    };

    /// <summary>
    /// Builds a child by cloning this node's model and appending one bound.
    /// relation is LessEqual for the floor branch and GreaterEqual for the ceiling branch.
    /// </summary>
    public BranchNode Branch(int variableIndex, RelationType relation, double bound, int childNumber)
    {
        string varName = Model.VariableNames.Length > variableIndex
            ? Model.VariableNames[variableIndex]
            : $"x{variableIndex + 1}";

        string symbol = relation == RelationType.LessEqual ? "<=" : ">=";
        string description = $"{varName} {symbol} {bound:0.###}";

        var child = new BranchNode
        {
            Label = $"{Label}.{childNumber}",
            Model = Model.WithExtraConstraint(
                Model.UnitRow(variableIndex), relation, bound, description),
            Parent = this,
            BranchDescription = description
        };

        Children.Add(child);
        return child;
    }

    public override string ToString() => $"Node {Label} ({BranchDescription})";
}

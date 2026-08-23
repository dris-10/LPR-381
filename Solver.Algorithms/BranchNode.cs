using System.Collections.Generic;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Algorithms;

public sealed class BranchNode
{
    public required string Label { get; init; } //the node identifier like 1.2 

    /// The LP relaxation solved at this node, including all inherited branching cuts.
    public required LPModel Model { get; init; }

    public BranchNode? Parent { get; init; }
    public int Depth => Parent is null ? 0 : Parent.Depth + 1;

    public string BranchDescription { get; init; } = "root"; //displaying the tablue clearly

    //Result of solving this node's relaxation. Set by the driver.
    public SolutionResult? Relaxation { get; set; }

    //Why this node stopped: solved integral, pruned by bound, or infeasible.
    public string Outcome { get; set; } = "unexplored";

    public List<BranchNode> Children { get; } = new();

    //Creates the root node from the original model.
    public static BranchNode Root(LPModel model) => new()
    {
        Label = "1",
        Model = model.Clone(),
        BranchDescription = "root"
    };


    public BranchNode Branch(int variableIndex, RelationType relation, double bound, int childNumber,
                             string? displayAs = null) //clones the model and adds a constraint to it, then returns a new BranchNode with that model
    {
        string varName = Model.VariableNames.Length > variableIndex
            ? Model.VariableNames[variableIndex]
            : $"x{variableIndex + 1}";

        string symbol = relation == RelationType.LessEqual ? "<=" : ">="; //variable branch number
        string description = displayAs ?? $"{varName} {symbol} {bound:0.###}";

        var child = new BranchNode //here is the child
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

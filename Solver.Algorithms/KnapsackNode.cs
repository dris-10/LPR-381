using System.Collections.Generic;

namespace Solver.Algorithms;

/// <summary>
/// OWNER: Person 3. One node of the knapsack Branch and Bound tree.
/// Person 2 created this stub only so the solution compiles - do not implement it.
/// </summary>
public sealed class KnapsackNode
{
    public string Label { get; set; } = "0";

    /// <summary>Fixed decisions so far: item index -> 0 or 1. Unlisted items are still free.</summary>
    public Dictionary<int, int> FixedItems { get; } = new();

    public double UpperBound { get; set; }
    public double Value { get; set; }
    public double UsedCapacity { get; set; }
    public string Outcome { get; set; } = "unexplored";
}

namespace Solver.Core.Results;

public enum SolutionStatus
{
    Optimal,
    Infeasible,
    Unbounded,
    /// <summary>Safety valve: the iteration cap was hit (cycling or a bug).</summary>
    IterationLimit,
    /// <summary>Branch and Bound / Cutting Plane stopped early but has an incumbent.</summary>
    SuboptimalIncumbent
}

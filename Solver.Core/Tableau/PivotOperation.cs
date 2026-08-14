namespace Solver.Core;

/// <summary>
/// A record of one pivot, so the output file can explain WHY each iteration happened.
/// The rubric wants visible working, not just final numbers.
/// </summary>
public sealed class PivotOperation
{
    public required int PivotRow { get; init; }
    public required int PivotColumn { get; init; }
    public required string EnteringVariable { get; init; }
    public required string LeavingVariable { get; init; }
    public double MinRatio { get; init; }
    public double PivotElement { get; init; }

    public override string ToString() =>
        $"{EnteringVariable} enters, {LeavingVariable} leaves " +
        $"(pivot @ row {PivotRow}, col {PivotColumn} = {PivotElement:0.###}, ratio {MinRatio:0.###})";
}

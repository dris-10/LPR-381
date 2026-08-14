namespace Solver.Core;

/// <summary>
/// An immutable frozen copy of a tableau at one point in time, plus a label.
/// The output writer walks these to print every iteration.
/// </summary>
public sealed class TableauSnapshot
{
    public required string Label { get; init; }
    public required Tableau Snapshot { get; init; }

    /// <summary>The pivot that PRODUCED this tableau. Null for the initial canonical form.</summary>
    public PivotOperation? Pivot { get; init; }

    /// <summary>Free-text note, e.g. "Phase 1 complete" or "Node 3 pruned by bound".</summary>
    public string? Note { get; init; }

    public static TableauSnapshot Of(string label, Tableau t, PivotOperation? pivot = null, string? note = null)
        => new() { Label = label, Snapshot = t.Clone(), Pivot = pivot, Note = note };
}

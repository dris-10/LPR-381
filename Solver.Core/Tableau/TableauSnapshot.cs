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

    /// <summary>Free-text note, e.g. "Phase 1 complete" or "Node 3 pruned by bound". Printed BEFORE the tableau.</summary>
    public string? Note { get; init; }

    /// <summary>
    /// Free-text block printed AFTER the tableau, one line per '\n'-separated segment.
    /// Used by Branch and Bound to report a node's outcome (branching / candidate / pruned)
    /// directly under its final tableau.
    /// </summary>
    public string? Footer { get; init; }

    /// <summary>Console-only color hint (see <see cref="SnapshotHighlight"/>). Null prints uncolored.</summary>
    public SnapshotHighlight? Highlight { get; init; }

    public static TableauSnapshot Of(string label, Tableau t, PivotOperation? pivot = null, string? note = null,
                                     string? footer = null, SnapshotHighlight? highlight = null)
        => new() { Label = label, Snapshot = t.Clone(), Pivot = pivot, Note = note, Footer = footer, Highlight = highlight };
}

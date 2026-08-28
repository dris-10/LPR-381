using System.Collections.Generic;
using Solver.Core;

namespace Solver.Core.Results;

/// <summary>One free-text log line, plus an optional console-only color hint.</summary>
public readonly record struct NoteEntry(string Text, SnapshotHighlight? Highlight = null)
{
    public override string ToString() => Text;
}

/// <summary>
/// Ordered record of everything an algorithm did. The output writer prints this verbatim.
/// Every algorithm MUST log the canonical form as the first entry.
/// </summary>
public sealed class IterationLog
{
    private readonly List<TableauSnapshot> _snapshots = new();
    private readonly List<NoteEntry> _notes = new();

    public IReadOnlyList<TableauSnapshot> Snapshots => _snapshots;
    public IReadOnlyList<NoteEntry> Notes => _notes;
    public int Count => _snapshots.Count;

    public void Add(string label, Tableau tableau, PivotOperation? pivot = null, string? note = null,
                    string? footer = null, SnapshotHighlight? highlight = null)
        => _snapshots.Add(TableauSnapshot.Of(label, tableau, pivot, note, footer, highlight));

    public void Add(TableauSnapshot snapshot) => _snapshots.Add(snapshot);

    /// <summary>Free-text line for things with no tableau, e.g. "Node 4 pruned: bound 12.5 &lt;= incumbent 13".</summary>
    public void Note(string text) => _notes.Add(new NoteEntry(text));

    /// <summary>Same as <see cref="Note(string)"/>, but colored on console (see <see cref="SnapshotHighlight"/>).</summary>
    public void Note(string text, SnapshotHighlight highlight) => _notes.Add(new NoteEntry(text, highlight));

    /// <summary>Merge a sub-solve's log into this one, prefixing each label. Used by Branch and Bound.</summary>
    public void Merge(IterationLog other, string prefix)
    {
        foreach (var s in other._snapshots)
            _snapshots.Add(new TableauSnapshot
            {
                Label = $"{prefix} {s.Label}",
                Snapshot = s.Snapshot,
                Pivot = s.Pivot,
                Note = s.Note,
                Footer = s.Footer,
                Highlight = s.Highlight
            });
        foreach (var n in other._notes) _notes.Add(new NoteEntry($"{prefix} {n.Text}", n.Highlight));
    }
}

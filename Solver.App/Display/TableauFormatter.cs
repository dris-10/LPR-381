using System;
using Solver.Core;
using Solver.Core.IO;
using Solver.Core.Results;

namespace Solver.App.Display;

/// <summary>Console rendering. Shares OutputFileWriter's formatting so screen and file agree.</summary>
public static class TableauFormatter
{
    public static void Print(Tableau t) => Console.Write(OutputFileWriter.RenderTableau(t));

    public static void PrintAllIterations(SolutionResult result)
    {
        var original = Console.ForegroundColor;

        foreach (var snap in result.Log.Snapshots)
        {
            if (snap.Highlight is { } highlight) Console.ForegroundColor = ColorFor(highlight);

            Console.WriteLine();
            Console.WriteLine(snap.Label);
            if (snap.Pivot is not null) Console.WriteLine($"  {snap.Pivot}");
            if (!string.IsNullOrWhiteSpace(snap.Note)) Console.WriteLine($"  {snap.Note}");
            Print(snap.Snapshot);

            if (!string.IsNullOrWhiteSpace(snap.Footer))
                foreach (var line in snap.Footer.Split('\n'))
                    Console.WriteLine($"  {line}");

            Console.ForegroundColor = original;
        }

        foreach (var note in result.Log.Notes)
        {
            if (note.Highlight is { } highlight) Console.ForegroundColor = ColorFor(highlight);
            Console.WriteLine($"  {note.Text}");
            Console.ForegroundColor = original;
        }
    }

    /// <summary>
    /// Green = a terminal, "cannot go any further" result (final optimal tableau, or a node
    ///         that becomes the new best candidate).
    /// Yellow = still in progress - not the final word on this line yet.
    /// Blue = a terminal candidate that did not beat the incumbent.
    /// Red = a dead end - infeasible, unbounded, or pruned by bound.
    /// </summary>
    private static ConsoleColor ColorFor(SnapshotHighlight highlight) => highlight switch
    {
        SnapshotHighlight.Best => ConsoleColor.Green,
        SnapshotHighlight.InProgress => ConsoleColor.Yellow,
        SnapshotHighlight.Candidate => ConsoleColor.Blue,
        SnapshotHighlight.DeadEnd => ConsoleColor.Red,
        _ => Console.ForegroundColor
    };
}

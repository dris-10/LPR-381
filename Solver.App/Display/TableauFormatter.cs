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
        foreach (var snap in result.Log.Snapshots)
        {
            Console.WriteLine();
            Console.WriteLine(snap.Label);
            if (snap.Pivot is not null) Console.WriteLine($"  {snap.Pivot}");
            if (!string.IsNullOrWhiteSpace(snap.Note)) Console.WriteLine($"  {snap.Note}");
            Print(snap.Snapshot);
        }

        foreach (var note in result.Log.Notes) Console.WriteLine($"  {note}");
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using Solver.Core.Models;
using Solver.Core.Results;

using Solver.Core;

namespace Solver.Core.IO;

/// <summary>
/// Writes the required output file: the canonical form, every tableau iteration,
/// and the final solution - all rounded to 3 decimal places.
/// </summary>
public static class OutputFileWriter
{
    private const int ColumnWidth = 9;

    public static void Write(string path, LPModel model, SolutionResult result)
        => File.WriteAllText(path, Render(model, result));

    public static string Render(LPModel model, SolutionResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine("==========================================================");
        sb.AppendLine($" ALGORITHM : {result.AlgorithmName}");
        sb.AppendLine($" GENERATED : {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine("==========================================================");
        sb.AppendLine();

        sb.AppendLine("INPUT MODEL");
        sb.AppendLine("-----------");
        sb.AppendLine(model.ToString());

        sb.AppendLine("ITERATIONS");
        sb.AppendLine("----------");
        foreach (var snap in result.Log.Snapshots)
        {
            sb.AppendLine(snap.Label);
            if (snap.Pivot is not null) sb.AppendLine($"  {snap.Pivot}");
            if (!string.IsNullOrWhiteSpace(snap.Note)) sb.AppendLine($"  {snap.Note}");
            sb.Append(RenderTableau(snap.Snapshot));
            if (!string.IsNullOrWhiteSpace(snap.Footer)) sb.Append(snap.Footer);
            sb.AppendLine();
        }

        if (result.Log.Notes.Count > 0)
        {
            sb.AppendLine("NOTES");
            sb.AppendLine("-----");
            foreach (var note in result.Log.Notes) sb.AppendLine($"  {note}");
            sb.AppendLine();
        }

        sb.AppendLine("RESULT");
        sb.AppendLine("------");
        sb.AppendLine($"Status : {result.Status}");
        if (!string.IsNullOrWhiteSpace(result.Message)) sb.AppendLine($"Message: {result.Message}");
        if (result.IsOptimal)
        {
            sb.AppendLine($"z      = {Math.Round(result.ObjectiveValue, 3):0.###}");
            for (int i = 0; i < result.VariableValues.Length; i++)
                sb.AppendLine($"{model.VariableNames.ElementAtOrDefault(i) ?? $"x{i + 1}",-6} = {Math.Round(result.VariableValues[i], 3):0.###}");
        }

        return sb.ToString();
    }

    /// <summary>Fixed-width tableau render, 3 decimals, with the basis column on the left.</summary>
    public static string RenderTableau(Tableau t)
    {
        var sb = new StringBuilder();

        sb.Append("basis".PadRight(7));
        foreach (var name in t.ColumnNames) sb.Append(name.PadLeft(ColumnWidth));
        sb.AppendLine();
        sb.AppendLine(new string('-', 7 + ColumnWidth * t.ColumnNames.Length));

        for (int r = 0; r < t.RowCount; r++)
        {
            string label = r == 0 ? "z" : t.ColumnNames[t.Basis[r - 1]];
            sb.Append(label.PadRight(7));
            for (int c = 0; c < t.TotalColumns; c++)
            {
                double v = Math.Round(t[r, c], 3);
                if (Math.Abs(v) < 1e-9) v = 0;          // kill "-0"
                sb.Append(v.ToString("0.###").PadLeft(ColumnWidth));
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

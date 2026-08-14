using System;
using System.Linq;
using Solver.Core;
using Solver.Core.IO;
using Solver.Core.Interfaces;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Algorithms;

/// <summary>
/// OWNER: Person 2.
///
/// Tableau-based Primal Simplex with a Phase 1 for models that need artificial variables.
///
/// Entering rule : Dantzig - most negative entry in the z-row, ties broken by lowest index.
/// Ratio test    : minimum of rhs / a[i,enter] over rows with a[i,enter] &gt; 0, ties by lowest row.
/// Termination   : no negative entry in the z-row.
///
/// Integer / binary restrictions are IGNORED here - this solves the LP relaxation.
/// Branch and Bound calls into this class to solve each node.
/// </summary>
public sealed class PrimalSimplex : ISolver
{
    public const int MaxIterations = 500;
    private const double Eps = Tableau.Epsilon;

    public string AlgorithmName => "Primal Simplex";

    /// <summary>Always true - integer models are solved as their LP relaxation.</summary>
    public bool CanSolve(LPModel model) => true;

    public SolutionResult Solve(LPModel model)
    {
        var log = new IterationLog();

        CanonicalForm canonical;
        try
        {
            canonical = CanonicalFormBuilder.Build(model);
        }
        catch (NotSupportedException ex)
        {
            return SolutionResult.Failure(SolutionStatus.Infeasible, AlgorithmName, ex.Message, log);
        }

        var t = canonical.Tableau;
        log.Add("Canonical Form", t);

        // ---------------- Phase 1 ----------------
        if (canonical.NeedsPhaseOne)
        {
            var phaseTwoObjective = SaveObjectiveRow(t);
            BuildPhaseOneObjective(t);
            log.Add("Phase 1 - Initial", t, note: "Minimising the sum of artificial variables.");

            var phase1 = Iterate(t, log, "Phase 1", forbidden: Array.Empty<int>());
            if (phase1 != SolutionStatus.Optimal)
                return SolutionResult.Failure(phase1, AlgorithmName,
                    "Phase 1 did not terminate normally.", log);

            if (Math.Abs(t.ObjectiveValue) > 1e-7)
                return SolutionResult.Failure(SolutionStatus.Infeasible, AlgorithmName,
                    $"Phase 1 ended with an artificial sum of {Math.Round(-t.ObjectiveValue, 3):0.###}. " +
                    "No feasible solution exists.", log);

            DriveOutArtificials(t, log);
            RestoreObjectiveRow(t, phaseTwoObjective);
            log.Add("Phase 2 - Initial", t, note: "Original objective restored and priced out against the basis.");
        }

        // ---------------- Phase 2 ----------------
        var status = Iterate(t, log, "Phase 2", forbidden: t.ArtificialColumns);

        if (status == SolutionStatus.Unbounded)
            return SolutionResult.Failure(SolutionStatus.Unbounded, AlgorithmName,
                "The entering column has no positive entry - the objective is unbounded.", log);

        if (status == SolutionStatus.IterationLimit)
            return SolutionResult.Failure(SolutionStatus.IterationLimit, AlgorithmName,
                $"Stopped after {MaxIterations} iterations (possible cycling).", log);

        log.Add("Optimal Tableau", t, note: "No negative entries remain in the z-row.");

        return new SolutionResult
        {
            Status = SolutionStatus.Optimal,
            AlgorithmName = AlgorithmName,
            ObjectiveValue = canonical.ToOriginalObjective(t.ObjectiveValue),
            VariableValues = t.ExtractDecisionValues(),
            Log = log,
            FinalTableau = t,
            SourceModel = model
        };
    }

    // ------------------------------------------------------------------
    // Core loop
    // ------------------------------------------------------------------

    /// <summary>Pivots until optimal, unbounded, or the iteration cap is hit.</summary>
    private static SolutionStatus Iterate(Tableau t, IterationLog log, string phase, int[] forbidden)
    {
        for (int iteration = 1; iteration <= MaxIterations; iteration++)
        {
            int enter = ChooseEnteringColumn(t, forbidden);
            if (enter < 0) return SolutionStatus.Optimal;

            int leave = ChooseLeavingRow(t, enter, out double ratio);
            if (leave < 0) return SolutionStatus.Unbounded;

            var pivot = new PivotOperation
            {
                PivotRow = leave,
                PivotColumn = enter,
                EnteringVariable = t.ColumnNames[enter],
                LeavingVariable = t.ColumnNames[t.Basis[leave - 1]],
                MinRatio = ratio,
                PivotElement = t[leave, enter]
            };

            t.Pivot(leave, enter);
            log.Add($"{phase} - Iteration {iteration}", t, pivot);
        }

        return SolutionStatus.IterationLimit;
    }

    /// <summary>Dantzig rule: most negative z-row entry. Returns -1 when optimal.</summary>
    private static int ChooseEnteringColumn(Tableau t, int[] forbidden)
    {
        int best = -1;
        double bestValue = -Eps;

        for (int c = 0; c < t.RhsColumn; c++)
        {
            if (forbidden.Contains(c)) continue;
            if (t[0, c] < bestValue)
            {
                bestValue = t[0, c];
                best = c;
            }
        }
        return best;
    }

    /// <summary>Minimum ratio test. Returns the matrix row to pivot on, or -1 if unbounded.</summary>
    private static int ChooseLeavingRow(Tableau t, int enter, out double bestRatio)
    {
        int best = -1;
        bestRatio = double.PositiveInfinity;

        for (int r = 1; r <= t.ConstraintCount; r++)
        {
            double a = t[r, enter];
            if (a <= Eps) continue;

            double ratio = t.Rhs(r) / a;
            if (ratio < bestRatio - Eps)
            {
                bestRatio = ratio;
                best = r;
            }
        }
        return best;
    }

    // ------------------------------------------------------------------
    // Phase 1 helpers
    // ------------------------------------------------------------------

    private static double[] SaveObjectiveRow(Tableau t)
    {
        var row = new double[t.TotalColumns];
        for (int c = 0; c < t.TotalColumns; c++) row[c] = t[0, c];
        return row;
    }

    /// <summary>
    /// Replaces the z-row with the Phase 1 objective (maximise the negative of the artificial sum),
    /// then prices out the artificials that are currently basic so their z-row entries become 0.
    /// </summary>
    private static void BuildPhaseOneObjective(Tableau t)
    {
        for (int c = 0; c < t.TotalColumns; c++) t[0, c] = 0.0;
        foreach (int a in t.ArtificialColumns) t[0, a] = 1.0;

        for (int r = 1; r <= t.ConstraintCount; r++)
        {
            if (!t.ArtificialColumns.Contains(t.Basis[r - 1])) continue;
            for (int c = 0; c < t.TotalColumns; c++) t[0, c] -= t[r, c];
        }
    }

    /// <summary>
    /// After a successful Phase 1 any artificial still in the basis sits at value zero.
    /// Pivot it out on any non-artificial column with a non-zero entry. If the whole row is
    /// zero the constraint is redundant and the artificial is left in place harmlessly.
    /// </summary>
    private static void DriveOutArtificials(Tableau t, IterationLog log)
    {
        for (int r = 1; r <= t.ConstraintCount; r++)
        {
            int basic = t.Basis[r - 1];
            if (!t.ArtificialColumns.Contains(basic)) continue;

            int replacement = -1;
            for (int c = 0; c < t.RhsColumn; c++)
            {
                if (t.ArtificialColumns.Contains(c)) continue;
                if (Math.Abs(t[r, c]) > Eps) { replacement = c; break; }
            }

            if (replacement < 0)
            {
                log.Note($"Row {r} is redundant - artificial {t.ColumnNames[basic]} stays basic at zero.");
                continue;
            }

            t.Pivot(r, replacement);
            log.Note($"Drove artificial {t.ColumnNames[basic]} out of the basis at zero level " +
                     $"(replaced by {t.ColumnNames[replacement]}).");
        }
    }

    /// <summary>Restores the original objective row and prices it out against the current basis.</summary>
    private static void RestoreObjectiveRow(Tableau t, double[] saved)
    {
        for (int c = 0; c < t.TotalColumns; c++) t[0, c] = saved[c];

        for (int r = 1; r <= t.ConstraintCount; r++)
        {
            int basic = t.Basis[r - 1];
            double factor = t[0, basic];
            if (Math.Abs(factor) < Eps) continue;
            for (int c = 0; c < t.TotalColumns; c++) t[0, c] -= factor * t[r, c];
        }
    }
}

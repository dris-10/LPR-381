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
/// Revised simplex: keeps an explicit B^-1 instead of the whole tableau, and rebuilds
/// each column / the objective row on demand from A, b, c and B^-1. A and b are cached
/// once from CanonicalFormBuilder's tableau and never touched again - only B^-1 and the
/// basis change from iteration to iteration.
///
/// Loop (mirrors the class doc originally written for this file):
///   1. y = cB.B^-1                                (the simplex multipliers / dual prices)
///   2. for each non-basic j: zj - cj = y.Aj - cj   (the PRICE OUT step - logged every iteration)
///   3. entering = the most negative zj - cj; if none are negative, stop - optimal
///   4. alpha = B^-1.A_enter                        (the entering column in the current basis)
///   5. ratio test on B^-1.b against alpha; no positive alpha entry means unbounded
///   6. update B^-1 with the product form of the inverse (Gauss-Jordan on B^-1 using alpha as
///      the pivot column), update the basis
///
/// Entering / ratio tie-breaks are copied verbatim from PrimalSimplex (lowest index / lowest
/// row) so both algorithms agree, per the class's original contract.
///
/// A "display tableau" - numerically identical to what PrimalSimplex would show at the same
/// point - is rebuilt from B^-1, A, b and the phase's cost vector purely for logging and for
/// FinalTableau (sensitivity analysis needs a real Tableau regardless of which algorithm solved it).
/// </summary>
public sealed class RevisedPrimalSimplex : ISolver
{
    public const int MaxIterations = 500;
    private const double Eps = Tableau.Epsilon;

    public string AlgorithmName => "Revised Primal Simplex";

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

        var seed = canonical.Tableau;
        int m = seed.ConstraintCount;
        int n = seed.DecisionVariableCount;
        int workCols = seed.RhsColumn;
        string[] names = seed.ColumnNames;
        int[] artificialColumns = seed.ArtificialColumns;

        // A and b, cached once from the untouched canonical tableau - never mutated again.
        var A = new double[m, workCols];
        var b = new double[m];
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < workCols; j++) A[i, j] = seed[i + 1, j];
            b[i] = seed.Rhs(i + 1);
        }

        // The working "maximise" objective (Min problems were already negated by the builder).
        var cMax = new double[workCols];
        for (int j = 0; j < workCols; j++) cMax[j] = -seed[0, j];

        var basis = (int[])seed.Basis.Clone();
        var binv = Identity(m);

        log.Add("Canonical Form", seed,
            note: "B is the starting identity basis, so B^-1 starts as the identity matrix.");

        // ---------------------------------------------------------------
        // Local helpers - all close over basis / binv, which mutate as we pivot.
        // ---------------------------------------------------------------

        double[] ComputeY(double[] cVec)
        {
            var y = new double[m];
            for (int k = 0; k < m; k++)
            {
                double s = 0;
                for (int i = 0; i < m; i++) s += cVec[basis[i]] * binv[i, k];
                y[k] = s;
            }
            return y;
        }

        double[] ComputeXB()
        {
            var xb = new double[m];
            for (int i = 0; i < m; i++)
            {
                double s = 0;
                for (int k = 0; k < m; k++) s += binv[i, k] * b[k];
                xb[i] = s;
            }
            return xb;
        }

        double[] ComputeAlpha(int col)
        {
            var alpha = new double[m];
            for (int i = 0; i < m; i++)
            {
                double s = 0;
                for (int k = 0; k < m; k++) s += binv[i, k] * A[k, col];
                alpha[i] = s;
            }
            return alpha;
        }

        double ReducedCost(double[] y, double[] cVec, int col)
        {
            double s = 0;
            for (int k = 0; k < m; k++) s += y[k] * A[k, col];
            return s - cVec[col];
        }

        Tableau BuildDisplay(double[] cVec, double[] y, double[] xb)
        {
            var t = new Tableau(m, workCols + 1, n, names) { ArtificialColumns = artificialColumns };
            Array.Copy(basis, t.Basis, m);

            double obj = 0;
            for (int i = 0; i < m; i++) obj += cVec[basis[i]] * xb[i];

            for (int j = 0; j < workCols; j++) t[0, j] = ReducedCost(y, cVec, j);
            t[0, t.RhsColumn] = obj;

            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < workCols; j++)
                {
                    double s = 0;
                    for (int k = 0; k < m; k++) s += binv[i, k] * A[k, j];
                    t[i + 1, j] = s;
                }
                t[i + 1, t.RhsColumn] = xb[i];
            }
            return t;
        }

        int ChooseEntering(double[] rc, int[] forbidden)
        {
            int best = -1;
            double bestValue = -Eps;
            for (int j = 0; j < workCols; j++)
            {
                if (forbidden.Contains(j) || basis.Contains(j)) continue;
                if (rc[j] < bestValue) { bestValue = rc[j]; best = j; }
            }
            return best;
        }

        int ChooseLeaving(double[] alpha, double[] xb, out double bestRatio)
        {
            int best = -1;
            bestRatio = double.PositiveInfinity;
            for (int i = 0; i < m; i++)
            {
                if (alpha[i] <= Eps) continue;
                double ratio = xb[i] / alpha[i];
                if (ratio < bestRatio - Eps) { bestRatio = ratio; best = i; }
            }
            return best;
        }

        void PivotBinv(int rowIdx, double[] alpha)
        {
            double pivotVal = alpha[rowIdx];
            for (int c = 0; c < m; c++) binv[rowIdx, c] /= pivotVal;
            for (int i = 0; i < m; i++)
            {
                if (i == rowIdx) continue;
                double factor = alpha[i];
                if (Math.Abs(factor) < Eps) continue;
                for (int c = 0; c < m; c++) binv[i, c] -= factor * binv[rowIdx, c];
            }
        }

        SolutionStatus RunPhase(double[] cVec, int[] forbidden, string phaseLabel)
        {
            for (int iter = 1; iter <= MaxIterations; iter++)
            {
                var y = ComputeY(cVec);
                var rc = new double[workCols];
                for (int j = 0; j < workCols; j++) rc[j] = ReducedCost(y, cVec, j);

                int enter = ChooseEntering(rc, forbidden);
                if (enter < 0) return SolutionStatus.Optimal;

                var alpha = ComputeAlpha(enter);
                var xb = ComputeXB();
                int leaveRow = ChooseLeaving(alpha, xb, out double ratio);
                if (leaveRow < 0) return SolutionStatus.Unbounded;

                string enterName = names[enter];
                string leaveName = names[basis[leaveRow]];

                string priceOut = string.Join("  ", Enumerable.Range(0, workCols)
                    .Where(j => !basis.Contains(j))
                    .Select(j => $"{names[j]}={Fmt(rc[j])}"));

                string ratioParts = string.Join("  ", Enumerable.Range(0, m)
                    .Where(i => alpha[i] > Eps)
                    .Select(i => $"{names[basis[i]]}: {Fmt(xb[i])}/{Fmt(alpha[i])}={Fmt(xb[i] / alpha[i])}"));

                var pivot = new PivotOperation
                {
                    PivotRow = leaveRow + 1,
                    PivotColumn = enter,
                    EnteringVariable = enterName,
                    LeavingVariable = leaveName,
                    MinRatio = ratio,
                    PivotElement = alpha[leaveRow]
                };

                string note =
                    $"y = cB.B^-1 = [{string.Join(", ", y.Select(Fmt))}]\n" +
                    $"  price out (z_j - c_j): {priceOut}\n" +
                    $"  {enterName} enters (most negative). alpha = B^-1.A_{enterName} = [{string.Join(", ", alpha.Select(Fmt))}]\n" +
                    $"  ratios: {ratioParts}  ->  {leaveName} leaves";

                PivotBinv(leaveRow, alpha);
                basis[leaveRow] = enter;

                var display = BuildDisplay(cVec, ComputeY(cVec), ComputeXB());
                log.Add($"{phaseLabel} - Iteration {iter}", display, pivot, note);
            }
            return SolutionStatus.IterationLimit;
        }

        void DriveOutArtificials()
        {
            for (int i = 0; i < m; i++)
            {
                if (!artificialColumns.Contains(basis[i])) continue;

                int replacement = -1;
                for (int j = 0; j < workCols; j++)
                {
                    if (artificialColumns.Contains(j)) continue;
                    double val = 0;
                    for (int k = 0; k < m; k++) val += binv[i, k] * A[k, j];
                    if (Math.Abs(val) > Eps) { replacement = j; break; }
                }

                if (replacement < 0)
                {
                    log.Note($"Row {i + 1} is redundant - artificial {names[basis[i]]} stays basic at zero.");
                    continue;
                }

                var alphaRep = ComputeAlpha(replacement);
                int oldBasic = basis[i];
                PivotBinv(i, alphaRep);
                basis[i] = replacement;
                log.Note($"Drove artificial {names[oldBasic]} out of the basis at zero level " +
                         $"(replaced by {names[replacement]}).");
            }
        }

        // ---------------- Phase 1 ----------------
        if (canonical.NeedsPhaseOne)
        {
            var cPhase1 = new double[workCols];
            foreach (int a in artificialColumns) cPhase1[a] = -1.0;

            var initDisplay = BuildDisplay(cPhase1, ComputeY(cPhase1), ComputeXB());
            log.Add("Phase 1 - Initial", initDisplay,
                note: "Cost vector swapped for -1 on every artificial; B^-1 is untouched.");

            var status1 = RunPhase(cPhase1, Array.Empty<int>(), "Phase 1");
            if (status1 == SolutionStatus.Unbounded)
                return SolutionResult.Failure(SolutionStatus.Unbounded, AlgorithmName,
                    "Phase 1 is unbounded, which should not happen for a feasibility LP - check the model.", log);
            if (status1 == SolutionStatus.IterationLimit)
                return SolutionResult.Failure(SolutionStatus.IterationLimit, AlgorithmName,
                    $"Stopped after {MaxIterations} iterations (possible cycling).", log);

            double phase1Obj = 0;
            var xbAfter = ComputeXB();
            for (int i = 0; i < m; i++) phase1Obj += cPhase1[basis[i]] * xbAfter[i];

            if (Math.Abs(phase1Obj) > 1e-7)
            {
                double artificialSum = -phase1Obj;
                return SolutionResult.Failure(SolutionStatus.Infeasible, AlgorithmName,
                    $"Phase 1 ended with an artificial sum of {Fmt(artificialSum)}. No feasible solution exists.", log);
            }

            DriveOutArtificials();

            var phase2Init = BuildDisplay(cMax, ComputeY(cMax), ComputeXB());
            log.Add("Phase 2 - Initial", phase2Init,
                note: "Original cost vector restored. Revised simplex re-prices from B^-1, so there is no objective row to patch up.");
        }

        // ---------------- Phase 2 ----------------
        var status2 = RunPhase(cMax, artificialColumns, "Phase 2");

        if (status2 == SolutionStatus.Unbounded)
            return SolutionResult.Failure(SolutionStatus.Unbounded, AlgorithmName,
                "The entering column has no positive entry in B^-1.A_enter - the objective is unbounded.", log);

        if (status2 == SolutionStatus.IterationLimit)
            return SolutionResult.Failure(SolutionStatus.IterationLimit, AlgorithmName,
                $"Stopped after {MaxIterations} iterations (possible cycling).", log);

        var finalTableau = BuildDisplay(cMax, ComputeY(cMax), ComputeXB());
        log.Add("Optimal Tableau", finalTableau,
            note: "No z_j - c_j is negative, so no column can improve the objective.");

        return new SolutionResult
        {
            Status = SolutionStatus.Optimal,
            AlgorithmName = AlgorithmName,
            ObjectiveValue = canonical.ToOriginalObjective(finalTableau.ObjectiveValue),
            VariableValues = finalTableau.ExtractDecisionValues(),
            Log = log,
            FinalTableau = finalTableau,
            SourceModel = model
        };
    }

    private static double[,] Identity(int size)
    {
        var m = new double[size, size];
        for (int i = 0; i < size; i++) m[i, i] = 1.0;
        return m;
    }

    private static string Fmt(double v)
    {
        double r = Math.Round(v, 3);
        if (Math.Abs(r) < 1e-9) r = 0;
        return r.ToString("0.###");
    }
}

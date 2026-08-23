using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Solver.Core;
using Solver.Core.IO;
using Solver.Core.Interfaces;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Algorithms;

public sealed class RevisedPrimalSimplex : ISolver
{
    private const double Eps = 1e-9;

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

        var start = canonical.Tableau;
        var s = new RevisedState(start);

        log.Add("Canonical Form", start,
            note: "B is the starting identity basis, so B^-1 starts as the identity matrix.");

        // Revised Phase one is the same loop as Phase 2, but with a different cost vector.
        if (canonical.NeedsPhaseOne)
        {
            var phaseOneCost = new double[s.ColumnCount];
            foreach (int a in s.ArtificialColumns) phaseOneCost[a] = -1.0;   // max -(sum of artificials)

            log.Add("Phase 1 - Initial", s.Materialise(phaseOneCost),
                note: "Cost vector swapped for -1 on every artificial; B^-1 is untouched.");

            var phase1 = Iterate(s, phaseOneCost, Array.Empty<int>(), "Phase 1", log);
            if (phase1 != SolutionStatus.Optimal)
                return SolutionResult.Failure(phase1, AlgorithmName,
                    "Phase 1 did not terminate normally.", log);

            double artificialSum = -s.Objective(phaseOneCost);
            if (artificialSum > 1e-7)
                return SolutionResult.Failure(SolutionStatus.Infeasible, AlgorithmName,
                    $"Phase 1 ended with an artificial sum of {Math.Round(artificialSum, 3):0.###}. " +
                    "No feasible solution exists.", log);

            DriveOutArtificials(s, log);

            log.Add("Phase 2 - Initial", s.Materialise(s.Cost),
                note: "Original cost vector restored. Revised simplex re-prices from B^-1, " +
                      "so there is no objective row to patch up.");
        }

        // Revised Phase 2 is the same loop as Phase 1, but with the original cost vector and forbidden columns.
        var status = Iterate(s, s.Cost, s.ArtificialColumns, "Phase 2", log);

        if (status == SolutionStatus.Unbounded)
            return SolutionResult.Failure(SolutionStatus.Unbounded, AlgorithmName,
                "The entering column has no positive entry in B^-1.A_enter - the objective is unbounded.", log);

        if (status == SolutionStatus.IterationLimit)
            return SolutionResult.Failure(SolutionStatus.IterationLimit, AlgorithmName,
                $"Stopped after {PrimalSimplex.MaxIterations} iterations (possible cycling).", log);

        var finalTableau = s.Materialise(s.Cost);
        log.Add("Optimal Tableau", finalTableau,
            note: "No z_j - c_j is negative, so no column can improve the objective.");

        return new SolutionResult
        {
            Status = SolutionStatus.Optimal,
            AlgorithmName = AlgorithmName,
            ObjectiveValue = canonical.ToOriginalObjective(s.Objective(s.Cost)),
            VariableValues = s.DecisionValues(),
            Log = log,
            FinalTableau = finalTableau,
            SourceModel = model
        };
    }


    private static SolutionStatus Iterate(RevisedState s, double[] cost, int[] forbidden,
                                          string phase, IterationLog log)
    {
        for (int iteration = 1; iteration <= PrimalSimplex.MaxIterations; iteration++) //primal simplex iterations
        {
            var y = s.Multipliers(cost);
            var reduced = s.PriceOut(y, cost, forbidden);

            int enter = ChooseEnteringColumn(reduced);
            if (enter < 0) return SolutionStatus.Optimal;

            var alpha = s.ColumnInBasis(enter);
            var xB = s.BasicValues();

            int leave = ChooseLeavingRow(xB, alpha, out double ratio);
            if (leave < 0) return SolutionStatus.Unbounded;

            var pivot = new PivotOperation
            {
                PivotRow = leave + 1,
                PivotColumn = enter,
                EnteringVariable = s.ColumnNames[enter],
                LeavingVariable = s.ColumnNames[s.Basis[leave]],
                MinRatio = ratio,
                PivotElement = alpha[leave]
            };

            string note = Working(s, y, reduced, enter, alpha, xB, leave);

            s.Update(leave, enter, alpha);
            log.Add($"{phase} - Iteration {iteration}", s.Materialise(cost), pivot, note);
        }

        return SolutionStatus.IterationLimit;
    }

    private static int ChooseEnteringColumn(IReadOnlyDictionary<int, double> reduced) // pivor colum with ratios
    {
        int best = -1;
        double bestValue = -Eps;

        foreach (var (column, value) in reduced.OrderBy(p => p.Key))
        {
            if (value < bestValue) //compares
            {
                bestValue = value;
                best = column;
            }
        }
        return best;
    }

  
    private static int ChooseLeavingRow(double[] xB, double[] alpha, out double bestRatio) //pivot row with ratios
    {
        int best = -1;
        bestRatio = double.PositiveInfinity;

        for (int i = 0; i < alpha.Length; i++)
        {
            if (alpha[i] <= Eps) continue;

            double ratio = xB[i] / alpha[i];
            if (ratio < bestRatio - Eps)
            {
                bestRatio = ratio;
                best = i;
            }
        }
        return best;
    }


    private static void DriveOutArtificials(RevisedState s, IterationLog log)
    {
        for (int r = 0; r < s.ConstraintCount; r++)
        {
            // Only care about rows where an artificial variable is still basic
            if (!s.ArtificialColumns.Contains(s.Basis[r])) continue;

            int replacement = -1;
            double[] chosen = Array.Empty<double>();

            // Look for any real, non-basic column that can pivot into this row
            for (int j = 0; j < s.ColumnCount; j++)
            {
                if (s.ArtificialColumns.Contains(j) || s.Basis.Contains(j)) continue;

                var candidate = s.ColumnInBasis(j);
                if (Math.Abs(candidate[r]) > Eps)
                {
                    replacement = j;
                    chosen = candidate;
                    break;
                }
            }
            // No usable pivot column -> row is redundant, artificial stays at zero
            if (replacement < 0)
            {
                log.Note($"Row {r + 1} is redundant - artificial {s.ColumnNames[s.Basis[r]]} stays basic at zero.");
                continue;
            }
            // Pivot the artificial out, replacement in, at zero level (degenerate pivot)
            log.Note($"Drove artificial {s.ColumnNames[s.Basis[r]]} out of the basis at zero level " +
                     $"(replaced by {s.ColumnNames[replacement]}).");
            s.Update(r, replacement, chosen);
        }
    }

    
    private static string Working(RevisedState s, double[] y, IReadOnlyDictionary<int, double> reduced,
                                  int enter, double[] alpha, double[] xB, int leave)
    {
        var sb = new StringBuilder();

        sb.Append("y = cB.B^-1 = ").Append(Vector(y));

        sb.Append("\n  price out (z_j - c_j): ")
          .Append(string.Join("  ", reduced.OrderBy(p => p.Key)
              .Select(p => $"{s.ColumnNames[p.Key]}={Num(p.Value)}")));

        sb.Append($"\n  {s.ColumnNames[enter]} enters (most negative). ")
          .Append($"alpha = B^-1.A_{s.ColumnNames[enter]} = ").Append(Vector(alpha));

        var ratios = new List<string>();
        for (int i = 0; i < alpha.Length; i++)
            if (alpha[i] > Eps)
                ratios.Add($"{s.ColumnNames[s.Basis[i]]}: {Num(xB[i])}/{Num(alpha[i])}={Num(xB[i] / alpha[i])}"); //price out variables

        sb.Append("\n  ratios: ").Append(ratios.Count == 0 ? "none positive" : string.Join("  ", ratios))
          .Append($"  ->  {s.ColumnNames[s.Basis[leave]]} leaves");

        return sb.ToString();
    }

    private static string Vector(double[] v) => "[" + string.Join(", ", v.Select(Num)) + "]";

    private static string Num(double v)
    {
        double r = Math.Round(v, 3);
        if (Math.Abs(r) < Eps) r = 0.0;   // never print "-0"
        return $"{r:0.###}";
    }


    private sealed class RevisedState
    {
        private readonly double[,] _a;      // original constraint matrix, m by n
        private readonly double[] _b;       // original rhs, length m
        private readonly double[,] _binv;   // current basis inverse, m by m
        private readonly int _decisionCount;

        public int ConstraintCount { get; }
        public int ColumnCount { get; }
        public int[] Basis { get; }
        public string[] ColumnNames { get; }
        public int[] ArtificialColumns { get; }

        /// The real cost vector, recovered from the canonical z-row (which holds -c).
        public double[] Cost { get; }

        public RevisedState(Tableau start)
        {
            ConstraintCount = start.ConstraintCount;
            ColumnCount = start.RhsColumn;               // everything except the rhs column
            _decisionCount = start.DecisionVariableCount;
            ColumnNames = start.ColumnNames;
            ArtificialColumns = start.ArtificialColumns;

            _a = new double[ConstraintCount, ColumnCount];
            _b = new double[ConstraintCount];
            Cost = new double[ColumnCount];

            for (int j = 0; j < ColumnCount; j++) Cost[j] = -start[0, j];

            for (int i = 0; i < ConstraintCount; i++)
            {
                for (int j = 0; j < ColumnCount; j++) _a[i, j] = start[i + 1, j];
                _b[i] = start[i + 1, start.RhsColumn];
            }

            Basis = (int[])start.Basis.Clone();

            // CanonicalFormBuilder always hands over an identity starting basis.
            _binv = new double[ConstraintCount, ConstraintCount];
            for (int i = 0; i < ConstraintCount; i++) _binv[i, i] = 1.0;
        }

        // xB = B^-1.b - the values of the basic variables.
        public double[] BasicValues()
        {
            var x = new double[ConstraintCount];
            for (int i = 0; i < ConstraintCount; i++)
            {
                double sum = 0;
                for (int k = 0; k < ConstraintCount; k++) sum += _binv[i, k] * _b[k];
                x[i] = sum;
            }
            return x;
        }

        // y = cB.B^-1 - the simplex multipliers for the given cost vector.
        public double[] Multipliers(double[] cost)
        {
            var y = new double[ConstraintCount];
            for (int i = 0; i < ConstraintCount; i++)
            {
                double sum = 0;
                for (int k = 0; k < ConstraintCount; k++) sum += cost[Basis[k]] * _binv[k, i];
                y[i] = sum;
            }
            return y;
        }

        // z_j - c_j for every non-basic, non-forbidden column.
        public Dictionary<int, double> PriceOut(double[] y, double[] cost, int[] forbidden)
        {
            var reduced = new Dictionary<int, double>();
            for (int j = 0; j < ColumnCount; j++)
            {
                if (Basis.Contains(j) || forbidden.Contains(j)) continue;

                double zj = 0;
                for (int i = 0; i < ConstraintCount; i++) zj += y[i] * _a[i, j];
                reduced[j] = zj - cost[j];
            }
            return reduced;
        }

        // alpha = B^-1.A_j - column j expressed in the current basis.
        public double[] ColumnInBasis(int j)
        {
            var alpha = new double[ConstraintCount];
            for (int i = 0; i < ConstraintCount; i++)
            {
                double sum = 0;
                for (int k = 0; k < ConstraintCount; k++) sum += _binv[i, k] * _a[k, j];
                alpha[i] = sum;
            }
            return alpha;
        }

        // Current objective value, cB.xB, which is also y.b.
        public double Objective(double[] cost)
        {
            var xB = BasicValues();
            double z = 0;
            for (int i = 0; i < ConstraintCount; i++) z += cost[Basis[i]] * xB[i];
            return z;
        }

        /// Product form of the inverse: the eta transformation that swaps the basic variable
        /// in row r for column q. This is a Gauss-Jordan sweep of B^-1 on the alpha column.
        public void Update(int r, int q, double[] alpha)
        {
            double pivot = alpha[r];
            if (Math.Abs(pivot) < Eps)
                throw new InvalidOperationException($"Degenerate pivot: alpha[{r}] is zero.");

            for (int k = 0; k < ConstraintCount; k++) _binv[r, k] /= pivot;

            for (int i = 0; i < ConstraintCount; i++)
            {
                if (i == r) continue;
                double factor = alpha[i];
                if (Math.Abs(factor) < Eps) continue;
                for (int k = 0; k < ConstraintCount; k++) _binv[i, k] -= factor * _binv[r, k];
            }

            Basis[r] = q;
        }

        public double[] DecisionValues()
        {
            var xB = BasicValues();
            var values = new double[_decisionCount];
            for (int i = 0; i < ConstraintCount; i++)
                if (Basis[i] < _decisionCount) values[Basis[i]] = xB[i];
            return values;
        }


        public Tableau Materialise(double[] cost) //rebuilds tableau from B^-1 for display purposes, not used in the algorithm itself
        {
            var t = new Tableau(ConstraintCount, ColumnCount + 1, _decisionCount, ColumnNames)
            {
                ArtificialColumns = ArtificialColumns
            };

            var y = Multipliers(cost);
            var xB = BasicValues();

            for (int j = 0; j < ColumnCount; j++)
            {
                double zj = 0;
                for (int i = 0; i < ConstraintCount; i++) zj += y[i] * _a[i, j];
                t[0, j] = zj - cost[j];
            }

            double objective = 0;
            for (int i = 0; i < ConstraintCount; i++) objective += cost[Basis[i]] * xB[i];
            t[0, t.RhsColumn] = objective;

            for (int i = 0; i < ConstraintCount; i++)
            {
                for (int j = 0; j < ColumnCount; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < ConstraintCount; k++) sum += _binv[i, k] * _a[k, j];
                    t[i + 1, j] = sum;
                }
                t[i + 1, t.RhsColumn] = xB[i];
                t.Basis[i] = Basis[i];
            }

            return t;
        }
    }
}

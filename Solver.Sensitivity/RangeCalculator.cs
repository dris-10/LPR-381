using System;
using System.Linq;
using Solver.Core;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Sensitivity;

/// <summary>
/// OWNER: Person 4. Objective-coefficient and RHS ranging, driven off the optimal tableau.
///
/// All of this rests on one fact: z_j - c_j = y . A_j, where A_j is a column's ORIGINAL
/// coefficients and y = cB . B^-1 is the current simplex multiplier vector. That identity is
/// invariant under pivoting, so at any point we can read a column's current tableau values as
/// exactly "B^-1 . A_j" without caring how many pivots produced the current basis.
/// </summary>
internal static class RangeCalculator
{
    /// <summary>
    /// +1 for a Max model, -1 for a Min model. CanonicalFormBuilder solves every model as a
    /// Max internally (it negates c when the model is a Min), so any value read straight off
    /// the z-row is on that internal scale and needs this to come back to the model's own.
    /// </summary>
    internal static int Sign(LPModel model) => model.Objective == ObjectiveType.Max ? 1 : -1;

    /// <summary>
    /// For every matrix row - in the FULL canonical form, i.e. including the implicit
    /// "x_j <= 1" rows CanonicalFormBuilder appends for binary variables - the column index
    /// that held that row's identity (initial basic) variable in the very first tableau:
    /// the slack for a <= row, the artificial for a >= or = row (its surplus column is skipped
    /// because a surplus is never part of the starting identity matrix).
    ///
    /// Only depends on the model's structure, not on any particular tableau, so it is valid
    /// for the optimal tableau of any solver that built its canonical form the same way.
    /// </summary>
    internal static int[] IdentityColumns(LPModel model)
    {
        int n = model.VariableCount;

        var relations = model.Constraints.Select(c => c.Relation).ToList();
        for (int j = 0; j < n; j++)
            if (model.SignRestrictions[j] == SignRestriction.Bin)
                relations.Add(RelationType.LessEqual);

        var identity = new int[relations.Count];
        int nextCol = n;
        for (int i = 0; i < relations.Count; i++)
        {
            switch (relations[i])
            {
                case RelationType.LessEqual:
                    identity[i] = nextCol;      // slack
                    nextCol += 1;
                    break;
                case RelationType.GreaterEqual:
                    identity[i] = nextCol + 1;  // surplus, then artificial
                    nextCol += 2;
                    break;
                default: // Equal
                    identity[i] = nextCol;      // artificial
                    nextCol += 1;
                    break;
            }
        }

        return identity;
    }

    /// <summary>Allowable range of a NON-BASIC decision variable's objective coefficient.</summary>
    internal static (double Lower, double Upper) NonBasicRange(SolutionResult result, int variableIndex)
    {
        var (t, model) = Require(result, variableIndex);

        double cBar = t[0, variableIndex];                 // reduced cost - >= 0 at optimum
        double cCurrent = model.ObjectiveCoefficients[variableIndex];

        // Internally c'_j may rise by at most cBar before this column would want to enter the
        // basis; it may fall without limit. ToOriginalRange folds that back through the sign.
        return ToOriginalRange(cCurrent, Sign(model), double.NegativeInfinity, cBar);
    }

    /// <summary>Allowable range of a BASIC decision variable's objective coefficient (standard ratio test).</summary>
    internal static (double Lower, double Upper) BasicRange(SolutionResult result, int variableIndex)
    {
        var (t, model) = Require(result, variableIndex);

        int row = Array.IndexOf(t.Basis, variableIndex);
        if (row < 0)
        {
            string name = model.VariableNames.ElementAtOrDefault(variableIndex) ?? $"x{variableIndex + 1}";
            throw new InvalidOperationException($"{name} is not basic in this solution - use the non-basic range instead.");
        }

        double deltaLower = double.NegativeInfinity;
        double deltaUpper = double.PositiveInfinity;

        for (int c = 0; c < t.RhsColumn; c++)
        {
            if (t.IsBasic(c)) continue;
            if (t.ArtificialColumns.Contains(c)) continue;   // never eligible to re-enter - ignore

            double a = t[row + 1, c];
            if (Math.Abs(a) < Tableau.Epsilon) continue;

            double ratio = t[0, c] / a;
            if (a > 0) deltaUpper = Math.Min(deltaUpper, ratio);
            else deltaLower = Math.Max(deltaLower, ratio);
        }

        double cCurrent = model.ObjectiveCoefficients[variableIndex];
        return ToOriginalRange(cCurrent, Sign(model), deltaLower, deltaUpper);
    }

    /// <summary>Allowable range of a constraint's RHS value that keeps the current basis feasible.</summary>
    internal static (double Lower, double Upper) RhsRange(SolutionResult result, int constraintIndex)
    {
        var t = result.FinalTableau ?? throw new InvalidOperationException("This result has no final tableau.");
        var model = result.SourceModel ?? throw new InvalidOperationException("This result has no source model.");

        if (constraintIndex < 0 || constraintIndex >= model.ConstraintCount)
            throw new ArgumentOutOfRangeException(nameof(constraintIndex));

        int identityColumn = IdentityColumns(model)[constraintIndex];

        double deltaLower = double.NegativeInfinity;
        double deltaUpper = double.PositiveInfinity;

        for (int r = 1; r <= t.ConstraintCount; r++)
        {
            double a = t[r, identityColumn];
            if (Math.Abs(a) < Tableau.Epsilon) continue;

            double ratio = -t.Rhs(r) / a;
            if (a > 0) deltaLower = Math.Max(deltaLower, ratio);
            else deltaUpper = Math.Min(deltaUpper, ratio);
        }

        double bCurrent = model.Constraints[constraintIndex].Rhs;
        return (bCurrent + deltaLower, bCurrent + deltaUpper);
    }

    /// <summary>Converts an internal delta range on c' back to a range on the model's own c.</summary>
    private static (double Lower, double Upper) ToOriginalRange(double cCurrent, int sign, double deltaLower, double deltaUpper)
    {
        double v1 = cCurrent + sign * deltaLower;
        double v2 = cCurrent + sign * deltaUpper;
        return (Math.Min(v1, v2), Math.Max(v1, v2));
    }

    private static (Tableau Tableau, LPModel Model) Require(SolutionResult result, int variableIndex)
    {
        var t = result.FinalTableau ?? throw new InvalidOperationException("This result has no final tableau.");
        var model = result.SourceModel ?? throw new InvalidOperationException("This result has no source model.");

        if (variableIndex < 0 || variableIndex >= model.VariableCount)
            throw new ArgumentOutOfRangeException(nameof(variableIndex));

        return (t, model);
    }
}

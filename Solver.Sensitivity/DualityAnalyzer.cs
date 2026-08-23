using System;
using System.Collections.Generic;
using System.Linq;
using Solver.Algorithms;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Sensitivity;

/// <summary>
/// OWNER: Person 4. Builds the dual, solves it, and reports strong vs weak duality.
///
/// Standard symmetric-form duality: one dual variable y_i per primal constraint, one dual
/// constraint per primal variable.
///
///   Primal MAX, x_j restricted  >=0 / <=0 / urs  ->  Dual MIN, constraint j is >= / <= / = c_j
///   Primal constraint i         <= / >= / =       ->  y_i restricted >=0 / <=0 / urs
///   Primal MIN mirrors this with every one of those relations reversed.
///
/// int/bin restrictions have no classical LP dual, so they are treated as their >=0 (int/bin
/// still means x_j >= 0) LP relaxation for the purposes of building the dual - this only
/// affects duality analysis, never the primal's own IP solution.
/// </summary>
internal static class DualityAnalyzer
{
    private const double Tolerance = 1e-6;

    internal static LPModel BuildDual(LPModel primal)
    {
        int m = primal.ConstraintCount;
        int n = primal.VariableCount;
        bool primalIsMax = primal.Objective == ObjectiveType.Max;

        var dualObjective = primal.Constraints.Select(c => c.Rhs).ToArray();

        var dualConstraints = new List<Constraint>();
        for (int j = 0; j < n; j++)
        {
            var dualRowCoefficients = new double[m];
            for (int i = 0; i < m; i++)
                dualRowCoefficients[i] = primal.Constraints[i].Coefficients[j];

            RelationType relation = primal.SignRestrictions[j] switch
            {
                SignRestriction.Negative => primalIsMax ? RelationType.LessEqual : RelationType.GreaterEqual,
                SignRestriction.Urs => RelationType.Equal,
                _ => primalIsMax ? RelationType.GreaterEqual : RelationType.LessEqual // Positive, Int, Bin
            };

            dualConstraints.Add(new Constraint
            {
                Coefficients = dualRowCoefficients,
                Relation = relation,
                Rhs = primal.ObjectiveCoefficients[j],
                Name = $"dual_x{j + 1}"
            });
        }

        var dualSignRestrictions = new SignRestriction[m];
        for (int i = 0; i < m; i++)
        {
            dualSignRestrictions[i] = primal.Constraints[i].Relation switch
            {
                RelationType.LessEqual => primalIsMax ? SignRestriction.Positive : SignRestriction.Negative,
                RelationType.GreaterEqual => primalIsMax ? SignRestriction.Negative : SignRestriction.Positive,
                _ => SignRestriction.Urs
            };
        }

        return new LPModel
        {
            Objective = primalIsMax ? ObjectiveType.Min : ObjectiveType.Max,
            ObjectiveCoefficients = dualObjective,
            Constraints = dualConstraints,
            SignRestrictions = dualSignRestrictions,
            VariableNames = Enumerable.Range(1, m).Select(i => $"y{i}").ToArray()
        };
    }

    internal static (SolutionResult DualResult, string Verdict) Analyse(LPModel primal, SolutionResult primalResult)
    {
        if (!primalResult.IsOptimal)
        {
            return (
                SolutionResult.Failure(primalResult.Status, "Dual (skipped)",
                    "The primal is not optimal, so there is nothing to compare against."),
                $"No duality verdict: the primal is {primalResult.Status}.");
        }

        var dualModel = BuildDual(primal);

        SolutionResult dualResult;
        try
        {
            dualResult = new PrimalSimplex().Solve(dualModel);
        }
        catch (NotSupportedException ex)
        {
            return (
                SolutionResult.Failure(SolutionStatus.IterationLimit, "Dual", ex.Message),
                "Could not solve the dual: it needs a <=0 or unrestricted variable, which the " +
                "current canonical-form builder does not support yet (see CanonicalFormBuilder TODO).");
        }

        string verdict;
        if (!dualResult.IsOptimal)
        {
            verdict = $"The dual is {dualResult.Status} even though the primal is optimal - " +
                      "double check the model; a correctly built dual of a bounded, feasible " +
                      "primal should also be optimal.";
        }
        else if (Math.Abs(dualResult.ObjectiveValue - primalResult.ObjectiveValue) < Tolerance)
        {
            verdict = $"Strong duality holds: primal Z = dual Z = {Math.Round(primalResult.ObjectiveValue, 3):0.###}.";
        }
        else
        {
            verdict = "Weak duality only: both are optimal but the objective values differ " +
                      $"(primal Z = {Math.Round(primalResult.ObjectiveValue, 3):0.###}, " +
                      $"dual Z = {Math.Round(dualResult.ObjectiveValue, 3):0.###}).";
        }

        return (dualResult, verdict);
    }
}

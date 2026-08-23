using System;
using System.Linq;
using Solver.Algorithms;
using Solver.Core.Interfaces;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Sensitivity;

/// <summary>
/// OWNER: Person 4. Applies coefficient / RHS / activity / constraint changes and re-solves.
///
/// Every method here is non-destructive: the model handed in is cloned before anything is
/// touched, so the caller's original result stays valid and two "what if" questions asked in
/// a row never contaminate each other.
///
/// The re-solve goes through Pick(): an integer model wants Branch and Bound, everything else
/// goes to the Primal Simplex. While B&amp;B is still a stub the LP relaxation is solved instead
/// and the result says so in its Message, rather than throwing in the user's face.
/// </summary>
internal static class ModelModifier
{
    internal static SolutionResult ApplyObjectiveChange(LPModel model, int variableIndex, double newCoefficient)
    {
        RequireVariableIndex(model, variableIndex);

        var changed = model.Clone();
        double old = changed.ObjectiveCoefficients[variableIndex];
        changed.ObjectiveCoefficients[variableIndex] = newCoefficient;

        return Resolve(changed,
            $"c{variableIndex + 1} changed from {old:0.###} to {newCoefficient:0.###}");
    }

    internal static SolutionResult ApplyRhsChange(LPModel model, int constraintIndex, double newRhs)
    {
        RequireConstraintIndex(model, constraintIndex);

        var changed = model.Clone();
        var original = changed.Constraints[constraintIndex];
        double old = original.Rhs;

        changed.Constraints[constraintIndex] = new Constraint
        {
            Coefficients = original.Coefficients,
            Relation = original.Relation,
            Rhs = newRhs,
            Name = original.Name
        };

        return Resolve(changed,
            $"RHS of constraint {constraintIndex + 1} changed from {old:0.###} to {newRhs:0.###}");
    }

    /// <summary>
    /// Adds a new decision variable (column). columnCoefficients holds its coefficient in each
    /// existing constraint, in constraint order. The new variable is taken as x &gt;= 0.
    /// </summary>
    internal static SolutionResult AddActivity(LPModel model, double objectiveCoefficient, double[] columnCoefficients)
    {
        ArgumentNullException.ThrowIfNull(columnCoefficients);

        if (columnCoefficients.Length != model.ConstraintCount)
            throw new ArgumentException(
                $"A new activity needs one coefficient per constraint: expected {model.ConstraintCount}, got {columnCoefficients.Length}.",
                nameof(columnCoefficients));

        var source = model.Clone();
        int newIndex = source.VariableCount;

        var constraints = source.Constraints
            .Select((c, i) => new Constraint
            {
                Coefficients = c.Coefficients.Append(columnCoefficients[i]).ToArray(),
                Relation = c.Relation,
                Rhs = c.Rhs,
                Name = c.Name
            })
            .ToList();

        string newName = $"x{newIndex + 1}";

        var changed = new LPModel
        {
            Objective = source.Objective,
            ObjectiveCoefficients = source.ObjectiveCoefficients.Append(objectiveCoefficient).ToArray(),
            Constraints = constraints,
            SignRestrictions = source.SignRestrictions.Append(SignRestriction.Positive).ToArray(),
            VariableNames = source.VariableNames.Length == newIndex
                ? source.VariableNames.Append(newName).ToArray()
                : Enumerable.Range(1, newIndex + 1).Select(i => $"x{i}").ToArray()
        };

        return Resolve(changed,
            $"New activity {newName} added with objective coefficient {objectiveCoefficient:0.###}");
    }

    internal static SolutionResult AddConstraint(LPModel model, Constraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);

        if (constraint.Coefficients.Length != model.VariableCount)
            throw new ArgumentException(
                $"A new constraint needs one coefficient per variable: expected {model.VariableCount}, got {constraint.Coefficients.Length}.",
                nameof(constraint));

        string name = string.IsNullOrWhiteSpace(constraint.Name)
            ? $"c{model.ConstraintCount + 1}"
            : constraint.Name;

        var changed = model.WithExtraConstraint(
            constraint.Coefficients, constraint.Relation, constraint.Rhs, name);

        return Resolve(changed, $"New constraint {name} added: {constraint}");
    }

    /// <summary>Solves the modified model and records what was changed in the result's Message.</summary>
    private static SolutionResult Resolve(LPModel changed, string description)
    {
        var (result, note) = Solve(changed);

        return new SolutionResult
        {
            Status = result.Status,
            AlgorithmName = result.AlgorithmName,
            ObjectiveValue = result.ObjectiveValue,
            VariableValues = result.VariableValues,
            Log = result.Log,
            FinalTableau = result.FinalTableau,
            SourceModel = result.SourceModel ?? changed,
            Message = Join(description, note, result.Message)
        };
    }

    /// <summary>
    /// Runs the right solver for the modified model. An integer model belongs to Branch and
    /// Bound; until that is implemented the LP relaxation is solved and the caller is told.
    /// </summary>
    private static (SolutionResult Result, string Note) Solve(LPModel model)
    {
        if (model.HasIntegerRestrictions)
        {
            ISolver integerSolver = new KnapsackBranchAndBound().CanSolve(model)
                ? new KnapsackBranchAndBound()
                : new BranchAndBoundSimplex();

            try
            {
                return (integerSolver.Solve(model), string.Empty);
            }
            catch (NotImplementedException)
            {
                return (new PrimalSimplex().Solve(model),
                    $"{integerSolver.AlgorithmName} is not implemented yet, so this is the LP " +
                    "relaxation - the variable values are not guaranteed to be integral.");
            }
        }

        return (new PrimalSimplex().Solve(model), string.Empty);
    }

    private static string Join(params string[] parts)
        => string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static void RequireVariableIndex(LPModel model, int index)
    {
        if (index < 0 || index >= model.VariableCount)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"Variable index {index} is outside the model's {model.VariableCount} variables.");
    }

    private static void RequireConstraintIndex(LPModel model, int index)
    {
        if (index < 0 || index >= model.ConstraintCount)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"Constraint index {index} is outside the model's {model.ConstraintCount} constraints.");
    }
}

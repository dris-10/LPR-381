using System;
using System.Collections.Generic;
using System.Linq;
using Solver.Core;
using Solver.Core.Interfaces;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Algorithms;

/// <summary>
/// Gomory Cutting Plane algorithm for pure integer programming models.
///
/// The algorithm repeatedly:
///
///   1. Solves the LP relaxation.
///   2. Checks whether all integer-restricted variables are integral.
///   3. Finds a fractional basic integer variable.
///   4. Builds a Gomory fractional cut from its tableau row.
///   5. Adds the cut to the model.
///   6. Solves the new LP relaxation.
///
/// The process stops when:
///   - an integer solution is found,
///   - the LP becomes infeasible,
///   - the iteration limit is reached.
///
/// This implementation is intended for pure integer models whose
/// canonical form uses non-negative integer variables.
/// </summary>
public sealed class CuttingPlane : ISolver
{
    private const double IntegerEps = 1e-6;
    private const int MaxCuts = 100;

    public string AlgorithmName => "Cutting Plane (Gomory)";

    public bool CanSolve(LPModel model) =>
        model.HasIntegerRestrictions;

    public SolutionResult Solve(LPModel model)
    {
        if (!CanSolve(model))
        {
            return SolutionResult.Failure(
                SolutionStatus.Infeasible,
                AlgorithmName,
                "Cutting Plane requires an integer programming model.");
        }

        // ------------------------------------------------------------
        // Gomory fractional cuts require integer variables.
        //
        // For this implementation we require every decision variable
        // to be integer/binary and non-negative.
        // ------------------------------------------------------------

        for (int i = 0; i < model.VariableCount; i++)
        {
            var restriction = model.SignRestrictions[i];

            if (restriction != SignRestriction.Int &&
                restriction != SignRestriction.Bin)
            {
                return SolutionResult.Failure(
                    SolutionStatus.Infeasible,
                    AlgorithmName,
                    "The Gomory implementation requires all decision variables " +
                    "to be integer or binary and non-negative.");
            }
        }

        var log = new IterationLog();

        LPModel workingModel = model.Clone();

        SolutionResult? lastResult = null;

        for (int cutNumber = 0; cutNumber <= MaxCuts; cutNumber++)
        {
            log.Note(
                $"Cutting Plane iteration {cutNumber}: " +
                "solving current LP relaxation.");

            // --------------------------------------------------------
            // Solve current LP relaxation.
            // --------------------------------------------------------

            SolutionResult relaxation;

            try
            {
                relaxation = new RevisedPrimalSimplex().Solve(workingModel);
            }
            catch (Exception ex)
            {
                string exMessage = $"Failed to solve LP relaxation: {ex.Message}";
                log.Note(exMessage, SnapshotHighlight.DeadEnd);
                return SolutionResult.Failure(
                    SolutionStatus.Infeasible,
                    AlgorithmName,
                    exMessage,
                    log);
            }

            lastResult = relaxation;

            // --------------------------------------------------------
            // Check whether the LP solution is already integer, BEFORE
            // logging, so the closing snapshot can be colored: green if
            // this is the final answer, yellow if more cuts are coming.
            // --------------------------------------------------------

            int fractionalVariable = relaxation.IsOptimal
                ? FindFractionalIntegerVariable(workingModel, relaxation.VariableValues)
                : -1;

            SnapshotHighlight relaxationHighlight = !relaxation.IsOptimal
                ? SnapshotHighlight.DeadEnd
                : fractionalVariable < 0
                    ? SnapshotHighlight.Best
                    : SnapshotHighlight.InProgress;

            // --------------------------------------------------------
            // Copy the revised simplex iterations into this algorithm's
            // log so the output contains the Price Out/Product Form
            // information.
            // --------------------------------------------------------

            AppendRelaxationLog(
                log,
                relaxation,
                cutNumber,
                relaxationHighlight);

            if (!relaxation.IsOptimal)
            {
                string message = $"LP relaxation became {relaxation.Status} after " +
                    $"{cutNumber} cutting-plane iteration(s).";
                log.Note(message, SnapshotHighlight.DeadEnd);
                return SolutionResult.Failure(
                    relaxation.Status,
                    AlgorithmName,
                    message,
                    log);
            }

            if (fractionalVariable < 0)
            {
                log.Note(
                    $"Cutting Plane finished: all integer variables are integral " +
                    $"after {cutNumber} cut(s).",
                    SnapshotHighlight.Best);

                return new SolutionResult
                {
                    Status = SolutionStatus.Optimal,
                    AlgorithmName = AlgorithmName,
                    ObjectiveValue = relaxation.ObjectiveValue,
                    VariableValues = relaxation.VariableValues,
                    Log = log,
                    FinalTableau = relaxation.FinalTableau,
                    SourceModel = workingModel,
                    Message =
                        $"Integer solution found after {cutNumber} Gomory cut(s)."
                };
            }

            string fractionalName =
                workingModel.VariableNames.ElementAtOrDefault(fractionalVariable)
                ?? $"x{fractionalVariable + 1}";

            double fractionalValue =
                relaxation.VariableValues[fractionalVariable];

            log.Note(
                $"Fractional variable found: {fractionalName} = " +
                $"{Fmt(fractionalValue)}.");

            // --------------------------------------------------------
            // Find the tableau row containing the fractional basic
            // integer variable.
            // --------------------------------------------------------

            var tableau = relaxation.FinalTableau;

            if (tableau == null)
            {
                const string message = "The LP relaxation did not provide a final tableau.";
                log.Note(message, SnapshotHighlight.DeadEnd);
                return SolutionResult.Failure(SolutionStatus.Infeasible, AlgorithmName, message, log);
            }

            int tableauRow =
                FindBasicVariableRow(tableau, fractionalVariable);

            if (tableauRow < 1)
            {
                // If the chosen fractional variable is not basic, choose
                // another fractional basic integer variable.
                int alternate =
                    FindFractionalBasicVariable(
                        workingModel,
                        relaxation,
                        tableau);

                if (alternate < 0)
                {
                    const string message = "A fractional solution exists, but no fractional " +
                        "basic integer variable could be found for a Gomory cut.";
                    log.Note(message, SnapshotHighlight.DeadEnd);
                    return SolutionResult.Failure(SolutionStatus.IterationLimit, AlgorithmName, message, log);
                }

                fractionalVariable = alternate;

                fractionalName =
                    workingModel.VariableNames.ElementAtOrDefault(fractionalVariable)
                    ?? $"x{fractionalVariable + 1}";

                fractionalValue =
                    relaxation.VariableValues[fractionalVariable];

                tableauRow =
                    FindBasicVariableRow(
                        tableau,
                        fractionalVariable);
            }

            // --------------------------------------------------------
            // Generate Gomory cut.
            // --------------------------------------------------------

            Constraint cut;

            try
            {
                cut = BuildGomoryCut(
                    workingModel,
                    tableau,
                    tableauRow);
            }
            catch (Exception ex)
            {
                string message = $"Could not construct a Gomory cut: {ex.Message}";
                log.Note(message, SnapshotHighlight.DeadEnd);
                return SolutionResult.Failure(SolutionStatus.IterationLimit, AlgorithmName, message, log);
            }

            string cutName = $"GomoryCut{cutNumber + 1}";

            cut = new Constraint
            {
                Coefficients = cut.Coefficients,
                Relation = cut.Relation,
                Rhs = cut.Rhs,
                Name = cutName
            };

            log.Note(
                $"Generated {cutName}: {cut}");

            // --------------------------------------------------------
            // Add the cut.
            // --------------------------------------------------------

            workingModel = workingModel.WithExtraConstraint(
                cut.Coefficients,
                cut.Relation,
                cut.Rhs,
                cut.Name);

            log.Note(
                $"{cutName} added. Re-solving the strengthened LP relaxation.");
        }

        string cappedMessage = $"Cutting Plane reached the maximum of {MaxCuts} cuts " +
            "without obtaining an integer solution.";
        log.Note(cappedMessage, SnapshotHighlight.DeadEnd);
        return SolutionResult.Failure(SolutionStatus.IterationLimit, AlgorithmName, cappedMessage, log);
    }

    // ================================================================
    // Find fractional variables
    // ================================================================

    private static int FindFractionalIntegerVariable(
        LPModel model,
        double[] values)
    {
        foreach (int i in model.IntegerVariableIndices)
        {
            double value = values[i];

            if (Math.Abs(value - Math.Round(value)) > IntegerEps)
                return i;
        }

        return -1;
    }

    private static int FindFractionalBasicVariable(
        LPModel model,
        SolutionResult result,
        Tableau tableau)
    {
        foreach (int variable in model.IntegerVariableIndices)
        {
            double value = result.VariableValues[variable];

            if (Math.Abs(value - Math.Round(value)) <= IntegerEps)
                continue;

            int row = FindBasicVariableRow(tableau, variable);

            if (row >= 1)
                return variable;
        }

        return -1;
    }

    private static int FindBasicVariableRow(
        Tableau tableau,
        int variableIndex)
    {
        for (int r = 0; r < tableau.Basis.Length; r++)
        {
            if (tableau.Basis[r] == variableIndex)
                return r + 1;
        }

        return -1;
    }

    // ================================================================
    // Gomory cut
    // ================================================================

    /// <summary>
    /// Creates a Gomory fractional cut from a fractional basic row.
    ///
    /// The tableau row has the form:
    ///
    ///       x_B + a1*x1 + a2*x2 + ... = b
    ///
    /// For a fractional RHS:
    ///
    ///       sum frac(ai) * xi >= frac(b)
    ///
    /// is the Gomory fractional cut.
    ///
    /// Only original decision-variable columns are retained here.
    /// This is appropriate for the pure-integer models used by the
    /// assignment, where the original variables are the integer
    /// variables and the generated slack variables are auxiliary.
    /// </summary>
    private static Constraint BuildGomoryCut(
        LPModel model,
        Tableau tableau,
        int row)
    {
        double rhs = tableau.Rhs(row);

        double fractionalRhs =
            Fraction(rhs);

        if (fractionalRhs <= IntegerEps)
        {
            throw new InvalidOperationException(
                "Selected tableau row does not have a sufficiently fractional RHS.");
        }

        var coefficients =
            new double[model.VariableCount];

        for (int j = 0; j < model.VariableCount; j++)
        {
            double coefficient = tableau[row, j];

            double fractionalCoefficient =
                Fraction(coefficient);

            coefficients[j] = fractionalCoefficient;
        }

        // Remove numerical noise.
        for (int j = 0; j < coefficients.Length; j++)
        {
            if (Math.Abs(coefficients[j]) < IntegerEps)
                coefficients[j] = 0;
        }

        if (coefficients.All(x => Math.Abs(x) < IntegerEps))
        {
            throw new InvalidOperationException(
                "Generated Gomory cut contains no original decision-variable coefficients.");
        }

        return new Constraint
        {
            Coefficients = coefficients,
            Relation = RelationType.GreaterEqual,
            Rhs = fractionalRhs,
            Name = "GomoryCut"
        };
    }

    /// <summary>
    /// Fractional part in [0,1).
    ///
    /// Negative tableau coefficients require special handling:
    ///
    /// frac(x) = x - floor(x)
    ///
    /// rather than x - trunc(x).
    /// </summary>
    private static double Fraction(double value)
    {
        double result = value - Math.Floor(value);

        if (Math.Abs(result) < IntegerEps ||
            Math.Abs(result - 1.0) < IntegerEps)
        {
            return 0;
        }

        return result;
    }

    // ================================================================
    // Logging
    // ================================================================

    private static void AppendRelaxationLog(
        IterationLog destination,
        SolutionResult relaxation,
        int cutNumber,
        SnapshotHighlight closingHighlight)
    {
        if (relaxation.Log == null)
            return;

        var snapshots = relaxation.Log.Snapshots;

        for (int i = 0; i < snapshots.Count; i++)
        {
            var snapshot = snapshots[i];
            bool isLast = i == snapshots.Count - 1;

            destination.Add(
                new TableauSnapshot
                {
                    Label =
                        $"Cut {cutNumber} - {snapshot.Label}",

                    Snapshot = snapshot.Snapshot,

                    Pivot = snapshot.Pivot,

                    Note = snapshot.Note,

                    Footer = snapshot.Footer,

                    // The relaxation's own "Optimal Tableau" would show green in isolation.
                    // Overridden here to reflect what that optimum means for Cutting Plane as a
                    // whole: still fractional (another cut is coming) is yellow, not green.
                    Highlight = isLast ? closingHighlight : snapshot.Highlight
                });
        }

        foreach (var note in relaxation.Log.Notes)
        {
            string text = $"Cut {cutNumber}: {note.Text}";
            if (note.Highlight is { } highlight) destination.Note(text, highlight);
            else destination.Note(text);
        }
    }

    // ================================================================
    // Formatting
    // ================================================================

    private static string Fmt(double value)
    {
        double rounded = Math.Round(value, 3);

        if (Math.Abs(rounded) < IntegerEps)
            rounded = 0;

        return rounded.ToString("0.###");
    }
}
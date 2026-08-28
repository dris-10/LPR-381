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
    /// Creates a Gomory fractional cut from a fractional basic row, expressed purely in terms
    /// of the original decision variables.
    ///
    /// The raw cut lives in the CURRENT tableau's column space - original variables AND that
    /// iteration's slack/surplus/artificial columns:
    ///
    ///       sum frac(a_rj) * (every column j) >= frac(b_r)
    ///
    /// Most of the actual fractional content usually sits on the slack/surplus columns, not
    /// the original variables: whenever both original variables of a row are already basic at
    /// the fractional vertex (the ordinary case for a small LP), their own row coefficients are
    /// exactly 0 or 1 by construction of Gauss-Jordan elimination - integers, so Fraction() of
    /// them is always 0. Dropping the slack/surplus columns therefore throws away the cut
    /// entirely on exactly the models this algorithm is meant to solve.
    ///
    /// Each slack/surplus column is substituted back into original-variable terms using its own
    /// defining row equation - an EXACT linear identity, applied AFTER Fraction() is taken, so
    /// the substitution itself introduces no rounding error:
    ///
    ///   &lt;= row i, slack s_i    (coefficient +1 in its own row):  s_i = rhs_i - A_i.x
    ///   &gt;= row i, surplus e_i  (coefficient -1 in its own row):  e_i = A_i.x - rhs_i
    ///
    /// Artificial columns are dropped rather than substituted: an artificial is 0 throughout the
    /// real feasible region (it isn't a real problem variable, just Phase 1 bookkeeping), so a
    /// term frac(a_rk) * (artificial) contributes exactly 0 to any cut over the real variables.
    ///
    /// ExpandRows mirrors CanonicalFormBuilder's row list (implicit bin upper bounds, negative-
    /// RHS flip) so this substitution's row-to-column mapping lines up with the tableau's.
    /// </summary>
    private static Constraint BuildGomoryCut(
        LPModel model,
        Tableau tableau,
        int row)
    {
        double rhs = tableau.Rhs(row);
        double fractionalRhs = Fraction(rhs);

        if (fractionalRhs <= IntegerEps)
        {
            throw new InvalidOperationException(
                "Selected tableau row does not have a sufficiently fractional RHS.");
        }

        var coefficients = new double[model.VariableCount];
        for (int j = 0; j < model.VariableCount; j++)
            coefficients[j] = Fraction(tableau[row, j]);

        double cutRhs = fractionalRhs;

        // Substitute each row's slack/surplus column back into original-variable terms, the
        // same order CanonicalFormBuilder assigned them in: one primary column per row, plus a
        // trailing artificial for >= and = rows.
        void Substitute(int column, double sign, double[] rowCoefficients, double rowRhs)
        {
            double frac = Fraction(tableau[row, column]);
            if (Math.Abs(frac) < IntegerEps) return;

            for (int j = 0; j < model.VariableCount; j++)
                coefficients[j] -= frac * sign * rowCoefficients[j];

            cutRhs -= frac * sign * rowRhs;
        }

        int col = model.VariableCount;
        foreach (var c in ExpandRows(model))
        {
            switch (c.Relation)
            {
                case RelationType.LessEqual:
                    Substitute(col, sign: 1.0, c.Coefficients, c.Rhs);
                    col++;
                    break;

                case RelationType.GreaterEqual:
                    Substitute(col, sign: -1.0, c.Coefficients, c.Rhs); // surplus
                    col++;
                    col++; // artificial: dropped, not substituted (see summary above)
                    break;

                default: // Equal
                    col++; // artificial only: dropped
                    break;
            }
        }

        // Remove numerical noise.
        for (int j = 0; j < coefficients.Length; j++)
        {
            if (Math.Abs(coefficients[j]) < IntegerEps)
                coefficients[j] = 0;
        }
        if (Math.Abs(cutRhs) < IntegerEps) cutRhs = 0;

        if (coefficients.All(x => Math.Abs(x) < IntegerEps))
        {
            throw new InvalidOperationException(
                "Generated Gomory cut contains no original decision-variable coefficients.");
        }

        return new Constraint
        {
            Coefficients = coefficients,
            Relation = RelationType.GreaterEqual,
            Rhs = cutRhs,
            Name = "GomoryCut"
        };
    }

    /// <summary>
    /// Mirrors CanonicalFormBuilder's row list exactly (frozen contract, see
    /// Solver.Core/IO/CanonicalFormBuilder.cs): the original constraints, with an implicit
    /// x_i &lt;= 1 row appended per binary variable, and any negative-RHS row flipped. Needed here
    /// so BuildGomoryCut's column-to-row mapping lines up with the tableau CanonicalFormBuilder
    /// actually produced for this model.
    /// </summary>
    private static List<Constraint> ExpandRows(LPModel model)
    {
        var rows = model.Constraints.Select(c => c.Clone()).ToList();

        for (int j = 0; j < model.VariableCount; j++)
        {
            if (model.SignRestrictions[j] != SignRestriction.Bin) continue;
            var unit = new double[model.VariableCount];
            unit[j] = 1.0;
            rows.Add(new Constraint { Coefficients = unit, Relation = RelationType.LessEqual, Rhs = 1.0 });
        }

        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Rhs >= 0) continue;
            rows[i] = new Constraint
            {
                Coefficients = rows[i].Coefficients.Select(v => -v).ToArray(),
                Relation = rows[i].Relation switch
                {
                    RelationType.LessEqual => RelationType.GreaterEqual,
                    RelationType.GreaterEqual => RelationType.LessEqual,
                    _ => RelationType.Equal
                },
                Rhs = -rows[i].Rhs
            };
        }

        return rows;
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
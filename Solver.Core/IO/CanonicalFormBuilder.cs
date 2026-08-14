using System;
using System.Collections.Generic;
using System.Linq;
using Solver.Core.Models;

using Solver.Core;

namespace Solver.Core.IO;

/// <summary>
/// Result of putting a model into canonical form: the starting tableau plus the
/// bookkeeping needed to translate answers back to the original model.
/// </summary>
public sealed class CanonicalForm
{
    public required Tableau Tableau { get; init; }

    /// <summary>True if the original model was a Min and we negated c to solve it as a Max.</summary>
    public required bool ObjectiveWasNegated { get; init; }

    /// <summary>True if any artificial variable was added, i.e. Phase 1 is needed.</summary>
    public bool NeedsPhaseOne => Tableau.ArtificialColumns.Length > 0;

    /// <summary>Convert a tableau objective value back to the original model's sign.</summary>
    public double ToOriginalObjective(double tableauZ) => ObjectiveWasNegated ? -tableauZ : tableauZ;
}

/// <summary>
/// FROZEN CONTRACT - Day 1. Turns an LPModel into the starting tableau.
///
/// Conventions:
///   Min is solved as Max by negating c. ToOriginalObjective() flips it back.
///   Negative RHS rows are multiplied by -1 and the relation is flipped first.
///   &lt;=  adds a slack     (+1)
///   &gt;=  adds a surplus   (-1) and an artificial (+1)
///   =   adds an artificial (+1)
///   bin variables get an implicit x_i &lt;= 1 row appended.
/// </summary>
public static class CanonicalFormBuilder
{
    public static CanonicalForm Build(LPModel model)
    {
        // TODO (Person 1): Negative and Urs sign restrictions are not yet handled.
        // Negative -> substitute x = -x'.  Urs -> substitute x = x+ - x-.
        // Every TestData file currently uses only +, int and bin.
        var unsupported = model.SignRestrictions
            .Select((s, i) => (s, i))
            .Where(t => t.s is SignRestriction.Negative or SignRestriction.Urs)
            .ToList();
        if (unsupported.Count > 0)
            throw new NotSupportedException(
                $"Sign restriction '{unsupported[0].s}' on {model.VariableNames.ElementAtOrDefault(unsupported[0].i) ?? $"x{unsupported[0].i + 1}"} " +
                "is not implemented yet. See CanonicalFormBuilder TODO.");

        int n = model.VariableCount;

        // Working copy of the constraint rows, with implicit binary upper bounds appended.
        var rows = model.Constraints.Select(c => c.Clone()).ToList();
        for (int j = 0; j < n; j++)
        {
            if (model.SignRestrictions[j] != SignRestriction.Bin) continue;
            var unit = new double[n];
            unit[j] = 1.0;
            rows.Add(new Constraint
            {
                Coefficients = unit,
                Relation = RelationType.LessEqual,
                Rhs = 1.0,
                Name = $"bin_{model.VariableNames.ElementAtOrDefault(j) ?? $"x{j + 1}"}"
            });
        }

        // Normalise negative RHS by flipping the whole row.
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
                Rhs = -rows[i].Rhs,
                Name = rows[i].Name
            };
        }

        int m = rows.Count;

        // Count the extra columns we need.
        int slackCount = 0, surplusCount = 0, artificialCount = 0;
        foreach (var c in rows)
        {
            switch (c.Relation)
            {
                case RelationType.LessEqual: slackCount++; break;
                case RelationType.GreaterEqual: surplusCount++; artificialCount++; break;
                default: artificialCount++; break;
            }
        }

        int totalColumns = n + slackCount + surplusCount + artificialCount + 1; // +1 for RHS

        // Build column names in order: decision, then slack/surplus/artificial as encountered, then rhs.
        var names = new string[totalColumns];
        for (int j = 0; j < n; j++)
            names[j] = model.VariableNames.ElementAtOrDefault(j) ?? $"x{j + 1}";
        names[totalColumns - 1] = "rhs";

        var t = new Tableau(m, totalColumns, n, names);

        bool negate = model.Objective == ObjectiveType.Min;

        // Objective row in  z - c.x = 0  form.
        for (int j = 0; j < n; j++)
        {
            double c = model.ObjectiveCoefficients[j];
            t[0, j] = negate ? c : -c;
        }

        // Constraint rows plus their slack / surplus / artificial columns.
        int nextCol = n;
        int sIdx = 1, eIdx = 1, aIdx = 1;
        var artificials = new List<int>();

        for (int i = 0; i < m; i++)
        {
            var c = rows[i];
            for (int j = 0; j < n; j++) t[i + 1, j] = c.Coefficients[j];
            t[i + 1, t.RhsColumn] = c.Rhs;

            switch (c.Relation)
            {
                case RelationType.LessEqual:
                    t[i + 1, nextCol] = 1.0;
                    names[nextCol] = $"s{sIdx++}";
                    t.Basis[i] = nextCol;
                    nextCol++;
                    break;

                case RelationType.GreaterEqual:
                    t[i + 1, nextCol] = -1.0;
                    names[nextCol] = $"e{eIdx++}";
                    nextCol++;
                    t[i + 1, nextCol] = 1.0;
                    names[nextCol] = $"a{aIdx++}";
                    artificials.Add(nextCol);
                    t.Basis[i] = nextCol;
                    nextCol++;
                    break;

                default: // Equal
                    t[i + 1, nextCol] = 1.0;
                    names[nextCol] = $"a{aIdx++}";
                    artificials.Add(nextCol);
                    t.Basis[i] = nextCol;
                    nextCol++;
                    break;
            }
        }

        t.ArtificialColumns = artificials.ToArray();

        return new CanonicalForm
        {
            Tableau = t,
            ObjectiveWasNegated = negate
        };
    }
}

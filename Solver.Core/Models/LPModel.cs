using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Solver.Core.Models;

/// <summary>
/// The parsed input file. FROZEN CONTRACT - do not change without telling the group.
///
/// Everything downstream (algorithms, sensitivity, display) reads this shape.
/// Branch and Bound relies on Clone() + AddConstraint() to build child nodes.
/// </summary>
public sealed class LPModel
{
    public required ObjectiveType Objective { get; init; }

    /// <summary>c vector. Length == number of decision variables.</summary>
    public required double[] ObjectiveCoefficients { get; init; }

    public required List<Constraint> Constraints { get; init; }

    /// <summary>One entry per decision variable, same order as ObjectiveCoefficients.</summary>
    public required SignRestriction[] SignRestrictions { get; init; }

    /// <summary>Display names: x1, x2, ... Assigned by the parser.</summary>
    public string[] VariableNames { get; set; } = System.Array.Empty<string>();

    public int VariableCount => ObjectiveCoefficients.Length;
    public int ConstraintCount => Constraints.Count;

    public bool HasIntegerRestrictions =>
        SignRestrictions.Any(s => s is SignRestriction.Int or SignRestriction.Bin);

    public bool IsPureBinary =>
        SignRestrictions.Length > 0 && SignRestrictions.All(s => s == SignRestriction.Bin);

    /// <summary>True if this variable must take an integer value in a feasible solution.</summary>
    public bool IsIntegerVariable(int index) =>
        SignRestrictions[index] is SignRestriction.Int or SignRestriction.Bin;

    /// <summary>Indices of every variable that must be integral. Used by B&amp;B branching rules.</summary>
    public IEnumerable<int> IntegerVariableIndices =>
        Enumerable.Range(0, VariableCount).Where(IsIntegerVariable);

    /// <summary>
    /// Deep copy. Branch and Bound MUST use this before adding a branching constraint,
    /// otherwise sibling nodes corrupt each other.
    /// </summary>
    public LPModel Clone() => new()
    {
        Objective = Objective,
        ObjectiveCoefficients = (double[])ObjectiveCoefficients.Clone(),
        Constraints = Constraints.Select(c => c.Clone()).ToList(),
        SignRestrictions = (SignRestriction[])SignRestrictions.Clone(),
        VariableNames = (string[])VariableNames.Clone()
    };

    /// <summary>
    /// Returns a clone with one extra constraint appended. Used for B&amp;B branching
    /// (x_i &lt;= floor, x_i &gt;= ceil) and for Cutting Plane (Gomory cuts).
    /// </summary>
    public LPModel WithExtraConstraint(double[] coefficients, RelationType relation, double rhs, string name)
    {
        var copy = Clone();
        copy.Constraints.Add(new Constraint
        {
            Coefficients = (double[])coefficients.Clone(),
            Relation = relation,
            Rhs = rhs,
            Name = name
        });
        return copy;
    }

    /// <summary>Convenience: a zero row with 1.0 in one position, for branching constraints.</summary>
    public double[] UnitRow(int variableIndex)
    {
        var row = new double[VariableCount];
        row[variableIndex] = 1.0;
        return row;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(Objective == ObjectiveType.Max ? "max " : "min ");
        sb.AppendLine(string.Join(" ", ObjectiveCoefficients.Select(c => c >= 0 ? $"+{c:0.###}" : $"{c:0.###}")));
        foreach (var c in Constraints) sb.AppendLine(c.ToString());
        sb.AppendLine(string.Join(" ", SignRestrictions.Select(s => s switch
        {
            SignRestriction.Positive => "+",
            SignRestriction.Negative => "-",
            SignRestriction.Urs => "urs",
            SignRestriction.Int => "int",
            _ => "bin"
        })));
        return sb.ToString();
    }
}

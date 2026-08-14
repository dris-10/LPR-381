using System.Linq;

namespace Solver.Core.Models;

/// <summary>One technology row of the model: a.x (relation) rhs</summary>
public sealed class Constraint
{
    public required double[] Coefficients { get; init; }
    public required RelationType Relation { get; init; }
    public required double Rhs { get; init; }

    /// <summary>Display label, e.g. "c1". Assigned by the parser.</summary>
    public string Name { get; set; } = string.Empty;

    public Constraint Clone() => new()
    {
        Coefficients = (double[])Coefficients.Clone(),
        Relation = Relation,
        Rhs = Rhs,
        Name = Name
    };

    public string RelationSymbol => Relation switch
    {
        RelationType.LessEqual => "<=",
        RelationType.GreaterEqual => ">=",
        _ => "="
    };

    public override string ToString() =>
        $"{string.Join(" ", Coefficients.Select(c => c >= 0 ? $"+{c:0.###}" : $"{c:0.###}"))} {RelationSymbol} {Rhs:0.###}";
}

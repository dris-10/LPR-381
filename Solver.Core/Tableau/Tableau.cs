using System;
using System.Linq;

namespace Solver.Core;

/// <summary>
/// FROZEN CONTRACT. The canonical tableau every algorithm reads and writes.
///
/// LAYOUT (agreed by the group - do not change silently):
///   Row 0            = objective (z) row, stored in  z - c.x = 0  form.
///                      For a MAX problem the entering variable is the MOST NEGATIVE
///                      entry in row 0. Optimal when no entry in row 0 is negative.
///   Rows 1 .. m      = constraint rows, one per constraint.
///   Column 0..n-1    = decision variables x1..xn
///   Column n..       = slack / surplus / artificial variables
///   LAST column      = RHS
///
/// Basis[i] is the column index of the basic variable for CONSTRAINT row i
/// (so Basis has length m, and Basis[0] belongs to matrix row 1).
/// </summary>
public sealed class Tableau
{
    /// <summary>Numerical tolerance used for all zero / sign comparisons across the project.</summary>
    public const double Epsilon = 1e-9;

    private readonly double[,] _m;

    public int ConstraintCount { get; }
    public int TotalColumns { get; }

    /// <summary>Column labels: x1, x2, s1, e1, a1, ... and "rhs" for the last column.</summary>
    public string[] ColumnNames { get; }

    /// <summary>Column index of the basic variable in each constraint row. Length == ConstraintCount.</summary>
    public int[] Basis { get; }

    /// <summary>Column indices that hold artificial variables (used by Phase 1 / feasibility checks).</summary>
    public int[] ArtificialColumns { get; set; } = Array.Empty<int>();

    /// <summary>Number of original decision variables (columns 0 .. DecisionVariableCount-1).</summary>
    public int DecisionVariableCount { get; }

    public Tableau(int constraintCount, int totalColumns, int decisionVariableCount, string[] columnNames)
    {
        if (columnNames.Length != totalColumns)
            throw new ArgumentException($"columnNames must have {totalColumns} entries, got {columnNames.Length}.");

        ConstraintCount = constraintCount;
        TotalColumns = totalColumns;
        DecisionVariableCount = decisionVariableCount;
        ColumnNames = columnNames;
        _m = new double[constraintCount + 1, totalColumns];
        Basis = new int[constraintCount];
    }

    /// <summary>Index of the RHS column.</summary>
    public int RhsColumn => TotalColumns - 1;

    /// <summary>Total rows including the objective row.</summary>
    public int RowCount => ConstraintCount + 1;

    /// <summary>row 0 is the objective row; rows 1..m are constraints.</summary>
    public double this[int row, int col]
    {
        get => _m[row, col];
        set => _m[row, col] = value;
    }

    /// <summary>RHS of a constraint row (1-based row index into the matrix).</summary>
    public double Rhs(int row) => _m[row, RhsColumn];

    /// <summary>Current objective value: the RHS of the z-row.</summary>
    public double ObjectiveValue => _m[0, RhsColumn];

    /// <summary>
    /// Standard Gauss-Jordan pivot. Normalises the pivot row then eliminates the
    /// pivot column from every other row INCLUDING the objective row, and updates the basis.
    /// pivotRow is a matrix row index and must be >= 1.
    /// </summary>
    public void Pivot(int pivotRow, int pivotCol)
    {
        if (pivotRow < 1 || pivotRow > ConstraintCount)
            throw new ArgumentOutOfRangeException(nameof(pivotRow), "Pivot row must be a constraint row (1..m).");

        double pivotElement = _m[pivotRow, pivotCol];
        if (Math.Abs(pivotElement) < Epsilon)
            throw new InvalidOperationException($"Degenerate pivot: element at ({pivotRow},{pivotCol}) is zero.");

        for (int c = 0; c < TotalColumns; c++)
            _m[pivotRow, c] /= pivotElement;

        // Guarantee an exact 1.0 in the pivot position (kills drift over many iterations).
        _m[pivotRow, pivotCol] = 1.0;

        for (int r = 0; r < RowCount; r++)
        {
            if (r == pivotRow) continue;
            double factor = _m[r, pivotCol];
            if (Math.Abs(factor) < Epsilon) continue;

            for (int c = 0; c < TotalColumns; c++)
                _m[r, c] -= factor * _m[pivotRow, c];

            _m[r, pivotCol] = 0.0;
        }

        Basis[pivotRow - 1] = pivotCol;
    }

    /// <summary>
    /// Reads the value of every decision variable out of the current basis.
    /// Non-basic variables are 0.
    /// </summary>
    public double[] ExtractDecisionValues()
    {
        var values = new double[DecisionVariableCount];
        for (int i = 0; i < ConstraintCount; i++)
        {
            int col = Basis[i];
            if (col >= 0 && col < DecisionVariableCount)
                values[col] = _m[i + 1, RhsColumn];
        }
        return values;
    }

    /// <summary>Value of any variable (decision, slack, surplus or artificial) by column index.</summary>
    public double ValueOf(int column)
    {
        for (int i = 0; i < ConstraintCount; i++)
            if (Basis[i] == column) return _m[i + 1, RhsColumn];
        return 0.0;
    }

    /// <summary>True if the given column is currently in the basis.</summary>
    public bool IsBasic(int column) => Basis.Contains(column);

    public Tableau Clone()
    {
        var copy = new Tableau(ConstraintCount, TotalColumns, DecisionVariableCount, (string[])ColumnNames.Clone())
        {
            ArtificialColumns = (int[])ArtificialColumns.Clone()
        };
        Array.Copy(Basis, copy.Basis, Basis.Length);
        for (int r = 0; r < RowCount; r++)
            for (int c = 0; c < TotalColumns; c++)
                copy[r, c] = _m[r, c];
        return copy;
    }

    /// <summary>Raw matrix copy, for the sensitivity module's matrix algebra.</summary>
    public double[,] ToArray()
    {
        var a = new double[RowCount, TotalColumns];
        Array.Copy(_m, a, _m.Length);
        return a;
    }
}

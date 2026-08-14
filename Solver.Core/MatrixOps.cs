using System;

namespace Solver.Core;

/// <summary>
/// Shared linear algebra. Used by Revised Primal Simplex (Person 2) to maintain B^-1,
/// and by Sensitivity Analysis (Person 4) for RHS ranging and shadow prices.
///
/// Written once, on purpose. Two hand-rolled versions of this will disagree on edge cases
/// and cost a day of debugging in week three.
/// </summary>
public static class MatrixOps
{
	private const double Eps = Tableau.Epsilon;

	/// <summary>n x n identity. The starting B^-1 when the initial basis is all slacks/artificials.</summary>
	public static double[,] Identity(int n)
	{
		var i = new double[n, n];
		for (int k = 0; k < n; k++) i[k, k] = 1.0;
		return i;
	}

	/// <summary>Matrix times column vector: A * x</summary>
	public static double[] Multiply(double[,] a, double[] x)
	{
		int rows = a.GetLength(0), cols = a.GetLength(1);
		if (cols != x.Length)
			throw new ArgumentException($"Cannot multiply {rows}x{cols} by vector of length {x.Length}.");

		var result = new double[rows];
		for (int r = 0; r < rows; r++)
		{
			double sum = 0;
			for (int c = 0; c < cols; c++) sum += a[r, c] * x[c];
			result[r] = sum;
		}
		return result;
	}

	/// <summary>Row vector times matrix: y * A. This is the simplex multiplier step, y = cB * B^-1.</summary>
	public static double[] Multiply(double[] y, double[,] a)
	{
		int rows = a.GetLength(0), cols = a.GetLength(1);
		if (rows != y.Length)
			throw new ArgumentException($"Cannot multiply vector of length {y.Length} by {rows}x{cols}.");

		var result = new double[cols];
		for (int c = 0; c < cols; c++)
		{
			double sum = 0;
			for (int r = 0; r < rows; r++) sum += y[r] * a[r, c];
			result[c] = sum;
		}
		return result;
	}

	/// <summary>Matrix times matrix: A * B</summary>
	public static double[,] Multiply(double[,] a, double[,] b)
	{
		int aRows = a.GetLength(0), aCols = a.GetLength(1), bCols = b.GetLength(1);
		if (aCols != b.GetLength(0))
			throw new ArgumentException("Inner dimensions do not match.");

		var result = new double[aRows, bCols];
		for (int r = 0; r < aRows; r++)
			for (int c = 0; c < bCols; c++)
			{
				double sum = 0;
				for (int k = 0; k < aCols; k++) sum += a[r, k] * b[k, c];
				result[r, c] = sum;
			}
		return result;
	}

	/// <summary>Dot product of two vectors.</summary>
	public static double Dot(double[] x, double[] y)
	{
		if (x.Length != y.Length) throw new ArgumentException("Vectors must be the same length.");
		double sum = 0;
		for (int i = 0; i < x.Length; i++) sum += x[i] * y[i];
		return sum;
	}

	/// <summary>
	/// Product form of the inverse. Updates B^-1 in place after the entering variable's
	/// column alpha = B^-1 * A_enter replaces the basic variable in row leavingRow.
	///
	/// This is the cheap O(m^2) revised-simplex update - no full inversion needed per iteration.
	/// leavingRow is 0-based into the basis (NOT a tableau matrix row).
	/// </summary>
	public static void EtaUpdate(double[,] bInverse, double[] alpha, int leavingRow)
	{
		int m = bInverse.GetLength(0);
		if (alpha.Length != m) throw new ArgumentException("alpha must have one entry per basis row.");

		double pivot = alpha[leavingRow];
		if (Math.Abs(pivot) < Eps)
			throw new InvalidOperationException($"Degenerate eta update: alpha[{leavingRow}] is zero.");

		for (int c = 0; c < m; c++) bInverse[leavingRow, c] /= pivot;

		for (int r = 0; r < m; r++)
		{
			if (r == leavingRow) continue;
			double factor = alpha[r];
			if (Math.Abs(factor) < Eps) continue;
			for (int c = 0; c < m; c++) bInverse[r, c] -= factor * bInverse[leavingRow, c];
		}
	}

	/// <summary>
	/// Full Gauss-Jordan inverse with partial pivoting. Person 4 needs this to rebuild B^-1
	/// from an arbitrary optimal basis. Throws if the matrix is singular.
	/// </summary>
	public static double[,] Invert(double[,] matrix)
	{
		int n = matrix.GetLength(0);
		if (matrix.GetLength(1) != n) throw new ArgumentException("Matrix must be square.");

		var a = (double[,])matrix.Clone();
		var inverse = Identity(n);

		for (int col = 0; col < n; col++)
		{
			int pivotRow = col;
			for (int r = col + 1; r < n; r++)
				if (Math.Abs(a[r, col]) > Math.Abs(a[pivotRow, col])) pivotRow = r;

			if (Math.Abs(a[pivotRow, col]) < Eps)
				throw new InvalidOperationException($"Matrix is singular - no pivot in column {col}.");

			if (pivotRow != col)
			{
				SwapRows(a, col, pivotRow);
				SwapRows(inverse, col, pivotRow);
			}

			double pivot = a[col, col];
			for (int c = 0; c < n; c++) { a[col, c] /= pivot; inverse[col, c] /= pivot; }

			for (int r = 0; r < n; r++)
			{
				if (r == col) continue;
				double factor = a[r, col];
				if (Math.Abs(factor) < Eps) continue;
				for (int c = 0; c < n; c++)
				{
					a[r, c] -= factor * a[col, c];
					inverse[r, c] -= factor * inverse[col, c];
				}
			}
		}
		return inverse;
	}

	/// <summary>Pulls one column out of a matrix, e.g. the entering variable's column A_j.</summary>
	public static double[] Column(double[,] a, int col)
	{
		int rows = a.GetLength(0);
		var result = new double[rows];
		for (int r = 0; r < rows; r++) result[r] = a[r, col];
		return result;
	}

	/// <summary>
	/// Builds the basis matrix B from the constraint coefficients and the current basis columns.
	/// constraintMatrix is m x (total columns), basis holds one column index per row.
	/// </summary>
	public static double[,] BasisMatrix(double[,] constraintMatrix, int[] basis)
	{
		int m = basis.Length;
		var b = new double[m, m];
		for (int r = 0; r < m; r++)
			for (int c = 0; c < m; c++)
				b[r, c] = constraintMatrix[r, basis[c]];
		return b;
	}

	private static void SwapRows(double[,] a, int r1, int r2)
	{
		int cols = a.GetLength(1);
		for (int c = 0; c < cols; c++) (a[r1, c], a[r2, c]) = (a[r2, c], a[r1, c]);
	}
}
using System;
using Solver.Core.Interfaces;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Algorithms;

/// <summary>
/// OWNER: Person 2.  STATUS: not implemented yet.
///
/// Revised simplex keeps B^-1 instead of the whole tableau and rebuilds columns on demand.
/// The marks here come from SHOWING the price-out steps, so log a snapshot at every stage.
///
/// Loop:
///   1. y = cB * B^-1                                (the simplex multipliers / dual prices)
///   2. for each non-basic j: zj - cj = y * Aj - cj  (the PRICE OUT step - log it)
///   3. entering = the most negative zj - cj; if none are negative, stop - optimal
///   4. alpha = B^-1 * A_enter                       (the entering column in current basis)
///   5. ratio test on B^-1 * b against alpha; no positive alpha entry means unbounded
///   6. update B^-1 with the product form of the inverse (eta matrix), update cB and the basis
///
/// Reuse: CanonicalFormBuilder for A, b, c and the starting basis, and reuse the
/// PrimalSimplex entering/ratio rules so both algorithms agree on tie-breaks.
/// </summary>
public sealed class RevisedPrimalSimplex : ISolver
{
    public string AlgorithmName => "Revised Primal Simplex";

    public bool CanSolve(LPModel model) => true;

    public SolutionResult Solve(LPModel model)
        => throw new NotImplementedException("Person 2: implement Revised Primal Simplex.");
}

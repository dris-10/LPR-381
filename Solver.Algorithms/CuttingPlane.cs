using System;
using Solver.Core.Interfaces;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Algorithms;

/// <summary>
/// OWNER: Person 3.  STATUS: not implemented yet.
/// Person 2 created this stub only so the solution compiles.
///
/// Solve the relaxation, pick a row whose RHS is fractional, generate the Gomory cut
/// from the fractional parts of that row, add it and re-solve. Repeat until integral.
/// </summary>
public sealed class CuttingPlane : ISolver
{
    public string AlgorithmName => "Cutting Plane (Gomory)";

    public bool CanSolve(LPModel model) => model.HasIntegerRestrictions;

    public SolutionResult Solve(LPModel model)
        => throw new NotImplementedException("Person 3: implement the Cutting Plane algorithm.");
}

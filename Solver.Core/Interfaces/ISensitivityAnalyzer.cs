using System.Collections.Generic;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Core.Interfaces;

/// <summary>
/// FROZEN CONTRACT - Day 1. Person 4 implements this against a fake SolutionResult
/// from Day 2 rather than waiting for the algorithms to be finished.
/// </summary>
public interface ISensitivityAnalyzer
{
    /// <summary>Allowable range of a NON-BASIC variable's objective coefficient.</summary>
    (double Lower, double Upper) NonBasicVariableRange(SolutionResult result, int variableIndex);

    /// <summary>Allowable range of a BASIC variable's objective coefficient.</summary>
    (double Lower, double Upper) BasicVariableRange(SolutionResult result, int variableIndex);

    /// <summary>Allowable range of a constraint's RHS value.</summary>
    (double Lower, double Upper) RhsRange(SolutionResult result, int constraintIndex);

    /// <summary>Shadow price per constraint, in constraint order.</summary>
    double[] ShadowPrices(SolutionResult result);

    /// <summary>Re-solve after changing one objective coefficient.</summary>
    SolutionResult ApplyObjectiveChange(SolutionResult result, int variableIndex, double newCoefficient);

    /// <summary>Re-solve after changing one RHS value.</summary>
    SolutionResult ApplyRhsChange(SolutionResult result, int constraintIndex, double newRhs);

    /// <summary>Add a new activity (column) and re-solve.</summary>
    SolutionResult AddActivity(SolutionResult result, double objectiveCoefficient, double[] columnCoefficients);

    /// <summary>Add a new constraint (row) and re-solve.</summary>
    SolutionResult AddConstraint(SolutionResult result, Constraint constraint);

    /// <summary>Build the dual of the primal model.</summary>
    LPModel BuildDual(LPModel primal);

    /// <summary>Solve the dual and report whether strong or weak duality holds.</summary>
    (SolutionResult DualResult, string DualityVerdict) AnalyseDuality(LPModel primal, SolutionResult primalResult);
}

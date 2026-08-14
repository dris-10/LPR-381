using System;
using Solver.Core.Interfaces;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Sensitivity;

/// <summary>
/// OWNER: Person 4.  STATUS: not implemented yet.
/// Person 1 created this stub so the solution compiles from Day 1.
///
/// Everything here is computed from result.FinalTableau and result.SourceModel.
/// Build against a hand-written fake SolutionResult from Day 2 - do not wait for the algorithms.
/// </summary>
public sealed class SensitivityAnalyzer : ISensitivityAnalyzer
{
    public (double Lower, double Upper) NonBasicVariableRange(SolutionResult result, int variableIndex)
        => throw new NotImplementedException("Person 4");

    public (double Lower, double Upper) BasicVariableRange(SolutionResult result, int variableIndex)
        => throw new NotImplementedException("Person 4");

    public (double Lower, double Upper) RhsRange(SolutionResult result, int constraintIndex)
        => throw new NotImplementedException("Person 4");

    public double[] ShadowPrices(SolutionResult result)
        => throw new NotImplementedException("Person 4");

    public SolutionResult ApplyObjectiveChange(SolutionResult result, int variableIndex, double newCoefficient)
        => throw new NotImplementedException("Person 4");

    public SolutionResult ApplyRhsChange(SolutionResult result, int constraintIndex, double newRhs)
        => throw new NotImplementedException("Person 4");

    public SolutionResult AddActivity(SolutionResult result, double objectiveCoefficient, double[] columnCoefficients)
        => throw new NotImplementedException("Person 4");

    public SolutionResult AddConstraint(SolutionResult result, Constraint constraint)
        => throw new NotImplementedException("Person 4");

    public LPModel BuildDual(LPModel primal)
        => throw new NotImplementedException("Person 4");

    public (SolutionResult DualResult, string DualityVerdict) AnalyseDuality(LPModel primal, SolutionResult primalResult)
        => throw new NotImplementedException("Person 4");
}

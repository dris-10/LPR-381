using System;
using Solver.Core.Interfaces;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Sensitivity;

/// <summary>
/// OWNER: Person 4.
///
/// Everything here is computed from result.FinalTableau and result.SourceModel - which every
/// ISolver populates on an Optimal result (see the SolutionResult contract). This class is a
/// thin facade; the real work lives in RangeCalculator, ShadowPriceCalculator, ModelModifier
/// and DualityAnalyzer.
///
/// Internally every algorithm solves as a MAX problem (CanonicalFormBuilder negates c for a
/// Min model), so every value pulled off the z-row is converted back to the model's own scale
/// before it is handed back - see RangeCalculator.Sign.
/// </summary>
public sealed class SensitivityAnalyzer : ISensitivityAnalyzer
{
    public (double Lower, double Upper) NonBasicVariableRange(SolutionResult result, int variableIndex)
        => RangeCalculator.NonBasicRange(result, variableIndex);

    public (double Lower, double Upper) BasicVariableRange(SolutionResult result, int variableIndex)
        => RangeCalculator.BasicRange(result, variableIndex);

    public (double Lower, double Upper) RhsRange(SolutionResult result, int constraintIndex)
        => RangeCalculator.RhsRange(result, constraintIndex);

    public double[] ShadowPrices(SolutionResult result)
        => ShadowPriceCalculator.Compute(result);

    public SolutionResult ApplyObjectiveChange(SolutionResult result, int variableIndex, double newCoefficient)
        => ModelModifier.ApplyObjectiveChange(RequireModel(result), variableIndex, newCoefficient);

    public SolutionResult ApplyRhsChange(SolutionResult result, int constraintIndex, double newRhs)
        => ModelModifier.ApplyRhsChange(RequireModel(result), constraintIndex, newRhs);

    public SolutionResult AddActivity(SolutionResult result, double objectiveCoefficient, double[] columnCoefficients)
        => ModelModifier.AddActivity(RequireModel(result), objectiveCoefficient, columnCoefficients);

    public SolutionResult AddConstraint(SolutionResult result, Constraint constraint)
        => ModelModifier.AddConstraint(RequireModel(result), constraint);

    public LPModel BuildDual(LPModel primal)
        => DualityAnalyzer.BuildDual(primal);

    public (SolutionResult DualResult, string DualityVerdict) AnalyseDuality(LPModel primal, SolutionResult primalResult)
        => DualityAnalyzer.Analyse(primal, primalResult);

    private static LPModel RequireModel(SolutionResult result)
        => result.SourceModel ?? throw new InvalidOperationException(
            "This result has no SourceModel - it was not Optimal, so sensitivity analysis is not possible.");
}

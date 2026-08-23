using System;
using Solver.Core.Results;

namespace Solver.Sensitivity;

/// <summary>
/// OWNER: Person 4. Shadow prices read from the z-row under each constraint's identity column
/// (the slack for a <= row, the artificial for a >= or = row - see RangeCalculator.IdentityColumns).
/// </summary>
internal static class ShadowPriceCalculator
{
    internal static double[] Compute(SolutionResult result)
    {
        var model = result.SourceModel ?? throw new InvalidOperationException("This result has no source model.");
        var t = result.FinalTableau ?? throw new InvalidOperationException("This result has no final tableau.");

        var identity = RangeCalculator.IdentityColumns(model);
        int sign = RangeCalculator.Sign(model);

        var prices = new double[model.ConstraintCount];
        for (int i = 0; i < model.ConstraintCount; i++)
            prices[i] = sign * t[0, identity[i]];

        return prices;
    }
}

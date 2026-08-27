using System;
using System.Collections.Generic;
using System.Linq;
using Solver.Core;
using Solver.Core.Interfaces;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Algorithms;

/// <summary>
/// Branch and Bound algorithm specifically for the 0/1 Knapsack problem.
///
/// The problem must contain:
///   - exactly one <= constraint
///   - every variable must be binary
///
/// The algorithm:
///   1. Sorts items according to value / weight.
///   2. Calculates a fractional upper bound for every node.
///   3. Branches on the next undecided item.
///   4. Uses backtracking through a stack.
///   5. Fathoms nodes that are infeasible or whose bound cannot beat
///      the current best candidate.
///
/// The algorithm is independent of the Simplex Branch & Bound algorithm.
/// </summary>
public sealed class KnapsackBranchAndBound : ISolver
{
    private const double Eps = 1e-9;

    public string AlgorithmName => "Branch and Bound (Knapsack)";

    /// <summary>
    /// Knapsack B&B is only valid for a pure binary problem
    /// with exactly one <= constraint.
    /// </summary>
    public bool CanSolve(LPModel model) =>
        model.IsPureBinary &&
        model.ConstraintCount == 1 &&
        model.Constraints[0].Relation == RelationType.LessEqual;

    public SolutionResult Solve(LPModel model)
    {
        if (!CanSolve(model))
        {
            return SolutionResult.Failure(
                SolutionStatus.Infeasible,
                AlgorithmName,
                "The Knapsack Branch and Bound algorithm requires a pure binary model " +
                "with exactly one <= constraint.");
        }

        var log = new IterationLog();

        int n = model.VariableCount;

        double capacity = model.Constraints[0].Rhs;
        double[] weights = model.Constraints[0].Coefficients;
        double[] values = model.ObjectiveCoefficients;

        // ------------------------------------------------------------
        // Sort items by value / weight.
        // ------------------------------------------------------------

        var items = Enumerable.Range(0, n)
            .Select(i => new Item
            {
                OriginalIndex = i,
                Value = values[i],
                Weight = weights[i],
                Ratio = Math.Abs(weights[i]) < Eps
                    ? double.PositiveInfinity
                    : values[i] / weights[i]
            })
            .OrderByDescending(x => x.Ratio)
            .ThenBy(x => x.OriginalIndex)
            .ToArray();

        log.Note("Knapsack Branch and Bound started.");
        log.Note(
            "Items sorted by value/weight ratio: " +
            string.Join(", ", items.Select(i =>
                $"{Name(model, i.OriginalIndex)} ({Fmt(i.Ratio)})")));

        // ------------------------------------------------------------
        // Incumbent / best candidate.
        // ------------------------------------------------------------

        double bestValue = model.Objective == ObjectiveType.Max
            ? double.NegativeInfinity
            : double.PositiveInfinity;

        int[] bestSolution = new int[n];

        bool foundCandidate = false;

        int explored = 0;
        int pruned = 0;
        int candidates = 0;
        int branchNumber = 0;

        // ------------------------------------------------------------
        // Depth-first Branch & Bound.
        //
        // state[k] = -1 -> undecided
        // state[k] =  0 -> item excluded
        // state[k] =  1 -> item included
        // ------------------------------------------------------------

        var root = new KnapsackNode
        {
            Level = 0,
            State = Enumerable.Repeat(-1, n).ToArray()
        };

        var stack = new Stack<KnapsackNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            explored++;

            double currentWeight = 0;
            double currentValue = 0;

            for (int k = 0; k < n; k++)
            {
                if (node.State[k] != 1)
                    continue;

                currentWeight += items[k].Weight;
                currentValue += items[k].Value;
            }

            string nodeName = $"Node {explored}";

            log.Note(
                $"{nodeName}: level={node.Level}, " +
                $"weight={Fmt(currentWeight)}, " +
                $"value={Fmt(currentValue)}");

            // --------------------------------------------------------
            // Infeasible node.
            // --------------------------------------------------------

            if (currentWeight > capacity + Eps)
            {
                pruned++;

                log.Note(
                    $"{nodeName} fathomed: infeasible because " +
                    $"weight {Fmt(currentWeight)} > capacity {Fmt(capacity)}.");

                continue;
            }

            // --------------------------------------------------------
            // Calculate fractional upper bound.
            // --------------------------------------------------------

            double bound = FractionalBound(
                items,
                node.State,
                node.Level,
                capacity,
                currentWeight,
                currentValue);

            log.Note(
                $"{nodeName}: fractional upper bound = {Fmt(bound)}.");

            // --------------------------------------------------------
            // Bound test.
            // --------------------------------------------------------

            if (foundCandidate &&
                model.Objective == ObjectiveType.Max &&
                bound <= bestValue + Eps)
            {
                pruned++;

                log.Note(
                    $"{nodeName} fathomed by bound: " +
                    $"bound {Fmt(bound)} <= incumbent {Fmt(bestValue)}.");

                continue;
            }

            // --------------------------------------------------------
            // All items decided -> integer candidate.
            // --------------------------------------------------------

            if (node.Level >= n)
            {
                candidates++;

                if (!foundCandidate ||
                    Improves(model.Objective, currentValue, bestValue))
                {
                    foundCandidate = true;
                    bestValue = currentValue;

                    for (int k = 0; k < n; k++)
                        bestSolution[items[k].OriginalIndex] =
                            node.State[k];

                    log.Note(
                        $"{nodeName}: Candidate {candidates} is the new " +
                        $"best candidate. z = {Fmt(bestValue)}.");

                    log.Note(
                        $"{nodeName}: " +
                        FormatSolution(model, bestSolution));
                }
                else
                {
                    log.Note(
                        $"{nodeName}: Candidate {candidates} discarded; " +
                        $"z = {Fmt(currentValue)}, incumbent = {Fmt(bestValue)}.");
                }

                continue;
            }

            // --------------------------------------------------------
            // Branch.
            //
            // We branch on the next item in ratio order.
            //
            // Include branch is pushed first, then exclude branch.
            // Because this is a stack, exclude is explored first.
            // --------------------------------------------------------

            int branchItem = node.Level;

            branchNumber++;

            log.Note(
                $"{nodeName}: branching on " +
                $"{Name(model, items[branchItem].OriginalIndex)} " +
                $"(value={Fmt(items[branchItem].Value)}, " +
                $"weight={Fmt(items[branchItem].Weight)}).");

            // Include item.
            var includeState = (int[])node.State.Clone();
            includeState[branchItem] = 1;

            var includeNode = new KnapsackNode
            {
                Level = node.Level + 1,
                State = includeState
            };

            // Exclude item.
            var excludeState = (int[])node.State.Clone();
            excludeState[branchItem] = 0;

            var excludeNode = new KnapsackNode
            {
                Level = node.Level + 1,
                State = excludeState
            };

            // Push include first, then exclude.
            // Therefore exclude is explored first.
            stack.Push(includeNode);
            stack.Push(excludeNode);

            log.Note(
                $"{nodeName}: created branches " +
                $"{Name(model, items[branchItem].OriginalIndex)} = 0 " +
                $"and {Name(model, items[branchItem].OriginalIndex)} = 1.");
        }

        // ------------------------------------------------------------
        // No feasible candidate.
        // ------------------------------------------------------------

        log.Note(
            $"Knapsack B&B finished. Explored {explored} node(s), " +
            $"pruned {pruned} node(s), found {candidates} candidate(s).");

        if (!foundCandidate)
        {
            return SolutionResult.Failure(
                SolutionStatus.Infeasible,
                AlgorithmName,
                "No feasible binary knapsack solution exists.",
                log);
        }

        log.Note(
            $"Best candidate: z = {Fmt(bestValue)}, " +
            $"{FormatSolution(model, bestSolution)}");

        // ------------------------------------------------------------
        // Build a final tableau for compatibility with the rest of the
        // application and sensitivity/output infrastructure.
        //
        // The actual optimisation was performed by this Knapsack B&B.
        // PrimalSimplex is only used to provide a final tableau.
        // ------------------------------------------------------------

        SolutionResult? finalRelaxation = null;

        try
        {
            finalRelaxation = new PrimalSimplex().Solve(model);
        }
        catch
        {
            // The knapsack solution itself remains valid.
        }

        return new SolutionResult
        {
            Status = SolutionStatus.Optimal,
            AlgorithmName = AlgorithmName,
            ObjectiveValue = bestValue,
            VariableValues = bestSolution.Select(x => (double)x).ToArray(),
            Log = log,
            FinalTableau = finalRelaxation?.FinalTableau,
            SourceModel = model,
            Message =
                $"Best candidate found by Knapsack Branch and Bound: " +
                $"z = {Fmt(bestValue)}. " +
                $"Explored {explored} nodes and pruned {pruned} nodes."
        };
    }

    // ================================================================
    // Fractional upper bound
    // ================================================================

    /// <summary>
    /// Calculates the classical fractional-knapsack upper bound.
    ///
    /// Items that are already included are counted fully.
    /// Undecided items are then taken greedily according to their
    /// value/weight ratio. The final item may be fractionally included.
    /// </summary>
    private static double FractionalBound(
        Item[] items,
        int[] state,
        int level,
        double capacity,
        double currentWeight,
        double currentValue)
    {
        double remaining = capacity - currentWeight;

        if (remaining < -Eps)
            return double.NegativeInfinity;

        double bound = currentValue;

        for (int k = level; k < items.Length; k++)
        {
            if (state[k] != -1)
                continue;

            var item = items[k];

            if (item.Weight <= Eps)
            {
                if (item.Value > 0)
                    bound += item.Value;

                continue;
            }

            if (item.Weight <= remaining + Eps)
            {
                remaining -= item.Weight;
                bound += item.Value;
            }
            else
            {
                // Fractional item.
                double fraction = remaining / item.Weight;

                if (fraction > 0)
                    bound += fraction * item.Value;

                break;
            }
        }

        return bound;
    }

    private static bool Improves(
        ObjectiveType objective,
        double candidate,
        double incumbent)
    {
        return objective == ObjectiveType.Max
            ? candidate > incumbent + Eps
            : candidate < incumbent - Eps;
    }

    private static string FormatSolution(
        LPModel model,
        int[] solution)
    {
        return string.Join(
            ", ",
            Enumerable.Range(0, solution.Length)
                .Select(i =>
                    $"{Name(model, i)} = {solution[i]}"));
    }

    private static string Name(LPModel model, int index)
    {
        return model.VariableNames.ElementAtOrDefault(index)
               ?? $"x{index + 1}";
    }

    private static string Fmt(double value)
    {
        double rounded = Math.Round(value, 3);

        if (Math.Abs(rounded) < Eps)
            rounded = 0;

        return rounded.ToString("0.###");
    }

    // ================================================================
    // Internal helper classes
    // ================================================================

    private sealed class Item
    {
        public int OriginalIndex { get; init; }
        public double Value { get; init; }
        public double Weight { get; init; }
        public double Ratio { get; init; }
    }

    private sealed class KnapsackNode
    {
        public int Level { get; init; }
        public int[] State { get; init; } = Array.Empty<int>();
    }
}
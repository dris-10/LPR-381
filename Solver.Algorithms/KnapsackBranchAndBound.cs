using System;
using System.Collections.Generic;
using System.Linq;
using Solver.Core;
using Solver.Core.Interfaces;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Algorithms;

/// <summary>
/// Specialized Branch and Bound algorithm for the 0/1 Knapsack problem.
///
/// Requirements:
/// - Maximization problem
/// - All variables must be binary
/// - Exactly one <= constraint
///
/// Algorithm:
/// 1. Sort items by value/weight ratio.
/// 2. Create the root node.
/// 3. Calculate a fractional knapsack upper bound.
/// 4. Branch by including or excluding the next item.
/// 5. Prune infeasible nodes.
/// 6. Prune nodes whose upper bound cannot beat the incumbent.
/// 7. Keep the best complete binary solution.
/// </summary>
public sealed class KnapsackBranchAndBound : ISolver
{
    private const double Eps = 1e-9;

    public string AlgorithmName => "Branch and Bound (Knapsack)";

    /// <summary>
    /// This algorithm only solves the standard 0/1 knapsack problem.
    /// </summary>
    public bool CanSolve(LPModel model)
    {
        return model.Objective == ObjectiveType.Max &&
               model.IsPureBinary &&
               model.ConstraintCount == 1 &&
               model.Constraints[0].Relation == RelationType.LessEqual;
    }

    public SolutionResult Solve(LPModel model)
    {
        if (!CanSolve(model))
        {
            return SolutionResult.Failure(
                SolutionStatus.Infeasible,
                AlgorithmName,
                "Knapsack Branch and Bound requires a MAX problem with " +
                "binary variables and exactly one <= constraint.");
        }

        var log = new IterationLog();

        int n = model.VariableCount;

        double capacity = model.Constraints[0].Rhs;
        double[] weights = model.Constraints[0].Coefficients;
        double[] values = model.ObjectiveCoefficients;

        // ------------------------------------------------------------
        // Create items and sort them by value/weight ratio.
        // ------------------------------------------------------------

        var items = new List<Item>();

        for (int i = 0; i < n; i++)
        {
            items.Add(new Item
            {
                OriginalIndex = i,
                Value = values[i],
                Weight = weights[i],
                Ratio = weights[i] == 0
                    ? double.PositiveInfinity
                    : values[i] / weights[i]
            });
        }

        items = items
            .OrderByDescending(x => x.Ratio)
            .ThenBy(x => x.OriginalIndex)
            .ToList();

        log.Note("Knapsack Branch and Bound started.");

        log.Note(
            "Items sorted by value/weight ratio: " +
            string.Join(", ",
                items.Select(item =>
                    $"{GetVariableName(model, item.OriginalIndex)} " +
                    $"(v={Fmt(item.Value)}, " +
                    $"w={Fmt(item.Weight)}, " +
                    $"ratio={Fmt(item.Ratio)})")));

        // ------------------------------------------------------------
        // Best solution found so far.
        // ------------------------------------------------------------

        double bestValue = 0.0;
        double[] bestSolution = new double[n];

        bool hasBestSolution = false;

        int exploredNodes = 0;
        int prunedNodes = 0;
        int candidateCount = 0;

        // ------------------------------------------------------------
        // Root node.
        //
        // Level = number of sorted items that have already been decided.
        //
        // Decisions:
        // -1 = not decided
        //  0 = item excluded
        //  1 = item included
        // ------------------------------------------------------------

        var root = new KnapsackNode
        {
            Level = 0,
            Decisions = Enumerable.Repeat(-1, n).ToArray(),
            CurrentWeight = 0,
            CurrentValue = 0
        };

        // Stack gives us depth-first search and backtracking.
        var stack = new Stack<KnapsackNode>();

        stack.Push(root);

        // ============================================================
        // MAIN BRANCH AND BOUND LOOP
        // ============================================================

        while (stack.Count > 0)
        {
            KnapsackNode node = stack.Pop();

            exploredNodes++;

            string nodeLabel = $"Node {exploredNodes}";

            log.Note(
                $"{nodeLabel}: Level {node.Level}, " +
                $"current weight = {Fmt(node.CurrentWeight)}, " +
                $"current value = {Fmt(node.CurrentValue)}.");

            // --------------------------------------------------------
            // STEP 1: Check feasibility.
            // --------------------------------------------------------

            if (node.CurrentWeight > capacity + Eps)
            {
                prunedNodes++;

                log.Note(
                    $"{nodeLabel} fathomed: infeasible because " +
                    $"weight {Fmt(node.CurrentWeight)} exceeds " +
                    $"capacity {Fmt(capacity)}.",
                    SnapshotHighlight.DeadEnd);

                continue;
            }

            // --------------------------------------------------------
            // STEP 2: Calculate fractional knapsack upper bound.
            // --------------------------------------------------------

            double bound = CalculateUpperBound(
                items,
                node,
                capacity);

            node.Bound = bound;

            log.Note(
                $"{nodeLabel}: upper bound = {Fmt(bound)}.");

            // --------------------------------------------------------
            // STEP 3: Prune by bound.
            // --------------------------------------------------------

            if (hasBestSolution && bound <= bestValue + Eps)
            {
                prunedNodes++;

                log.Note(
                    $"{nodeLabel} fathomed by bound: " +
                    $"upper bound {Fmt(bound)} cannot beat " +
                    $"best candidate {Fmt(bestValue)}.",
                    SnapshotHighlight.DeadEnd);

                continue;
            }

            // --------------------------------------------------------
            // STEP 4: If all items have been decided, this is an
            // integer candidate.
            // --------------------------------------------------------

            if (node.Level == n)
            {
                candidateCount++;

                log.Note(
                    $"{nodeLabel}: Candidate {candidateCount} found.");

                if (!hasBestSolution ||
                    node.CurrentValue > bestValue + Eps)
                {
                    hasBestSolution = true;

                    bestValue = node.CurrentValue;

                    bestSolution =
                        ConvertToOriginalOrder(
                            items,
                            node.Decisions,
                            n);

                    log.Note(
                        $"{nodeLabel}: Candidate {candidateCount} " +
                        $"is the new BEST candidate.",
                        SnapshotHighlight.Best);

                    log.Note(
                        $"Candidate solution: " +
                        FormatSolution(model, bestSolution),
                        SnapshotHighlight.Best);

                    log.Note(
                        $"Objective value z = {Fmt(bestValue)}.",
                        SnapshotHighlight.Best);
                }
                else
                {
                    log.Note(
                        $"{nodeLabel}: Candidate {candidateCount} " +
                        $"discarded because z = {Fmt(node.CurrentValue)} " +
                        $"does not beat incumbent z = {Fmt(bestValue)}.",
                        SnapshotHighlight.Candidate);
                }

                continue;
            }

            // --------------------------------------------------------
            // STEP 5: Branch on the next item.
            // --------------------------------------------------------

            Item item = items[node.Level];

            string variableName =
                GetVariableName(model, item.OriginalIndex);

            log.Note(
                $"{nodeLabel}: branching on {variableName}.",
                SnapshotHighlight.InProgress);

            // --------------------------------------------------------
            // Child 1: Exclude the item.
            //
            // x = 0
            // --------------------------------------------------------

            var excludeDecisions =
                (int[])node.Decisions.Clone();

            excludeDecisions[node.Level] = 0;

            var excludeNode = new KnapsackNode
            {
                Level = node.Level + 1,
                Decisions = excludeDecisions,
                CurrentWeight = node.CurrentWeight,
                CurrentValue = node.CurrentValue
            };

            // --------------------------------------------------------
            // Child 2: Include the item.
            //
            // x = 1
            // --------------------------------------------------------

            var includeDecisions =
                (int[])node.Decisions.Clone();

            includeDecisions[node.Level] = 1;

            var includeNode = new KnapsackNode
            {
                Level = node.Level + 1,
                Decisions = includeDecisions,
                CurrentWeight =
                    node.CurrentWeight + item.Weight,
                CurrentValue =
                    node.CurrentValue + item.Value
            };

            log.Note(
                $"{nodeLabel}: created two sub-problems:");

            log.Note(
                $"  Branch 1: {variableName} = 0");

            log.Note(
                $"  Branch 2: {variableName} = 1");

            // --------------------------------------------------------
            // Push INCLUDE first and EXCLUDE second.
            //
            // Since Stack is LIFO, the exclude branch will be explored
            // first. This gives depth-first traversal and naturally
            // demonstrates backtracking.
            // --------------------------------------------------------

            stack.Push(includeNode);
            stack.Push(excludeNode);
        }

        // ============================================================
        // FINAL RESULT
        // ============================================================

        log.Note(
            $"Search complete: explored {exploredNodes} node(s), " +
            $"pruned {prunedNodes} node(s), " +
            $"found {candidateCount} candidate(s).");

        if (!hasBestSolution)
        {
            const string message = "No feasible binary knapsack solution was found.";
            log.Note(message, SnapshotHighlight.DeadEnd);
            return SolutionResult.Failure(SolutionStatus.Infeasible, AlgorithmName, message, log);
        }

        log.Note(
            $"BEST CANDIDATE:",
            SnapshotHighlight.Best);

        log.Note(
            FormatSolution(model, bestSolution),
            SnapshotHighlight.Best);

        log.Note(
            $"Best objective value z = {Fmt(bestValue)}.",
            SnapshotHighlight.Best);

        return new SolutionResult
        {
            Status = SolutionStatus.Optimal,
            AlgorithmName = AlgorithmName,
            ObjectiveValue = bestValue,
            VariableValues = bestSolution,
            Log = log,
            FinalTableau = null,
            SourceModel = model,
            Message =
                $"Best candidate found after exploring {exploredNodes} " +
                $"nodes. Optimal z = {Fmt(bestValue)}."
        };
    }

    // ================================================================
    // FRACTIONAL UPPER BOUND
    // ================================================================

    /// <summary>
    /// Calculates the upper bound using the fractional knapsack idea.
    ///
    /// All remaining capacity is filled using the items with the
    /// highest value/weight ratio.
    ///
    /// The final item is allowed to be fractional only for calculating
    /// the bound. The actual solution remains binary.
    /// </summary>
    private static double CalculateUpperBound(
        List<Item> items,
        KnapsackNode node,
        double capacity)
    {
        if (node.CurrentWeight > capacity + Eps)
            return double.NegativeInfinity;

        double bound = node.CurrentValue;

        double remainingCapacity =
            capacity - node.CurrentWeight;

        for (int i = node.Level; i < items.Count; i++)
        {
            Item item = items[i];

            // --------------------------------------------------------
            // If the entire item fits, include all of its value in the
            // upper bound.
            // --------------------------------------------------------

            if (item.Weight <= remainingCapacity + Eps)
            {
                remainingCapacity -= item.Weight;

                bound += item.Value;
            }
            else
            {
                // ----------------------------------------------------
                // Only part of the item fits.
                //
                // This fractional value is used only to calculate an
                // optimistic upper bound.
                // ----------------------------------------------------

                if (item.Weight > Eps)
                {
                    double fraction =
                        remainingCapacity / item.Weight;

                    bound +=
                        item.Value * fraction;
                }

                break;
            }
        }

        return bound;
    }

    // ================================================================
    // CONVERT SORTED DECISIONS BACK TO ORIGINAL VARIABLE ORDER
    // ================================================================

    private static double[] ConvertToOriginalOrder(
        List<Item> items,
        int[] decisions,
        int variableCount)
    {
        var solution =
            new double[variableCount];

        for (int sortedIndex = 0;
             sortedIndex < items.Count;
             sortedIndex++)
        {
            int originalIndex =
                items[sortedIndex].OriginalIndex;

            solution[originalIndex] =
                decisions[sortedIndex] == 1
                    ? 1.0
                    : 0.0;
        }

        return solution;
    }

    // ================================================================
    // OUTPUT HELPERS
    // ================================================================

    private static string GetVariableName(
        LPModel model,
        int index)
    {
        return model.VariableNames
                   .ElementAtOrDefault(index)
               ?? $"x{index + 1}";
    }

    private static string FormatSolution(
        LPModel model,
        double[] solution)
    {
        return string.Join(
            ", ",
            Enumerable.Range(0, solution.Length)
                .Select(i =>
                    $"{GetVariableName(model, i)} = " +
                    $"{Fmt(solution[i])}"));
    }

    private static string Fmt(double value)
    {
        double rounded = Math.Round(value, 3);

        if (Math.Abs(rounded) < Eps)
            rounded = 0;

        return rounded.ToString("0.###");
    }

    // ================================================================
    // INTERNAL DATA STRUCTURES
    // ================================================================

    /// <summary>
    /// Represents one knapsack item.
    /// </summary>
    private sealed class Item
    {
        public int OriginalIndex { get; init; }

        public double Value { get; init; }

        public double Weight { get; init; }

        public double Ratio { get; init; }
    }

    /// <summary>
    /// Represents one node in the Branch and Bound tree.
    /// </summary>
    private sealed class KnapsackNode
    {
        /// <summary>
        /// Number of items that have already been decided.
        /// </summary>
        public int Level { get; init; }

        /// <summary>
        /// -1 = undecided
        ///  0 = excluded
        ///  1 = included
        ///
        /// Decisions are stored in value/weight sorted order.
        /// </summary>
        public int[] Decisions { get; init; }
            = Array.Empty<int>();

        public double CurrentWeight { get; init; }

        public double CurrentValue { get; init; }

        public double Bound { get; set; }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using Solver.Core;
using Solver.Core.Interfaces;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Algorithms;

/// <summary>
/// OWNER: Person 2.
///
/// Branch and Bound over LP relaxations solved by PrimalSimplex.
///
/// Driver:
///   1. root = BranchNode.Root(model); solve its relaxation with PrimalSimplex
///   2. if infeasible -> that branch is discarded (the whole problem is infeasible only if
///      every branch ends up discarded)
///   3. prune if the bound cannot beat the incumbent
///          (Max: relaxation z &lt;= incumbent z, Min: relaxation z &gt;= incumbent z)
///   4. if every integer-restricted variable is already integral -> a candidate; keep it if
///      it beats the incumbent, otherwise discard it
///   5. otherwise pick the lowest-index fractional integer variable and branch:
///          child 1: x_i &lt;= floor(value)   (explored first - depth-first, floor branch first)
///          child 2: x_i &gt;= ceil(value)
///   6. return the best candidate found (or Infeasible if none was)
///
/// Every node's PrimalSimplex log is folded into this algorithm's own log, re-labelled
/// "Table {node.Label}" (its canonical form gets a one-line header naming the node's
/// cumulative branching constraints; the rest keep their own labels prefixed with a dash).
/// A short footer explaining why the node closed (branching / candidate / pruned) is attached
/// under its last tableau. Use Tableau.Epsilon-scale tolerances when testing integrality - never ==.
/// </summary>
public sealed class BranchAndBoundSimplex : ISolver
{
    // Looser than Tableau.Epsilon (1e-9) on purpose: a value that has been carried through
    // several nodes' worth of Gauss-Jordan pivots can pick up more floating point noise than
    // a single tableau's own arithmetic, so integrality needs a slightly more forgiving test.
    private const double IntegerEps = 1e-6;

    public string AlgorithmName => "Branch and Bound (Simplex)";

    public bool CanSolve(LPModel model) => model.HasIntegerRestrictions;

    public SolutionResult Solve(LPModel model)
    {
        var log = new IterationLog();
        var relaxer = new PrimalSimplex();

        var stack = new Stack<BranchNode>();
        stack.Push(BranchNode.Root(model));

        SolutionResult? incumbent = null;
        BranchNode? incumbentNode = null;
        int exploredCount = 0;
        int candidateCount = 0;

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            exploredCount++;

            var result = relaxer.Solve(node.Model);
            node.Relaxation = result;

            string description = node.Parent is null ? "root LP relaxation" : Describe(node);
            string? footer = null;
            SnapshotHighlight highlight;

            if (!result.IsOptimal)
            {
                node.Outcome = $"{result.Status} - branch discarded";
                log.Note($"Table {node.Label}: {result.Status} - this branch is discarded.");
                highlight = SnapshotHighlight.DeadEnd;
            }
            else
            {
                double z = result.ObjectiveValue;
                string varsLine = VariablesLine(model, result);
                int fractional = FirstFractionalIndex(model, result.VariableValues);

                if (fractional < 0)
                {
                    // Integral: always judged as a candidate, win or lose, regardless of bound.
                    candidateCount++;
                    bool better = incumbent is null || Improves(model, z, incumbent.ObjectiveValue);
                    if (better)
                    {
                        incumbent = result;
                        incumbentNode = node;
                        node.Outcome = $"Candidate {candidateCount} - best so far";
                        footer = $"{varsLine}\nz value     = {Fmt(z)}\n" +
                                 $"This table is: Candidate {candidateCount} - best so far, z = {Fmt(z)}.";
                        highlight = SnapshotHighlight.Best;
                    }
                    else
                    {
                        node.Outcome = $"Candidate {candidateCount} - discarded";
                        footer = $"{varsLine}\nz value     = {Fmt(z)}\n" +
                                 $"This table is: Candidate {candidateCount} - z = {Fmt(z)} does not beat the incumbent z = {Fmt(incumbent!.ObjectiveValue)}, so it is discarded.";
                        highlight = SnapshotHighlight.Candidate;
                    }
                }
                else if (incumbent is not null && !Improves(model, z, incumbent.ObjectiveValue))
                {
                    // Fractional, but even its relaxed bound cannot beat the incumbent - no point branching.
                    node.Outcome = "pruned by bound";
                    footer = $"{varsLine}\nz value     = {Fmt(z)}\n" +
                             $"This table is: z = {Fmt(z)} cannot beat the incumbent z = {Fmt(incumbent.ObjectiveValue)}, so this branch is pruned.";
                    highlight = SnapshotHighlight.DeadEnd;
                }
                else
                {
                    double value = result.VariableValues[fractional];
                    double floor = Math.Floor(value + IntegerEps);
                    double ceil = Math.Ceiling(value - IntegerEps);
                    string varName = model.VariableNames.ElementAtOrDefault(fractional) ?? $"x{fractional + 1}";

                    node.Outcome = "branched";
                    footer = $"{varsLine}\nz value     = {Fmt(z)}\n" +
                             $"This table is: branching {varName} further ({varName} <= {Fmt(floor)} first, then {varName} >= {Fmt(ceil)})";
                    highlight = SnapshotHighlight.InProgress;

                    // Push ceiling then floor so the floor branch is explored first (depth-first).
                    var ceilChild = node.Branch(fractional, RelationType.GreaterEqual, ceil, 2);
                    var floorChild = node.Branch(fractional, RelationType.LessEqual, floor, 1);
                    stack.Push(ceilChild);
                    stack.Push(floorChild);
                }
            }

            AppendNodeLog(log, node, result, description, footer, highlight);
        }

        log.Note($"Explored {exploredCount} table(s) and found {candidateCount} candidate(s).");

        if (incumbent is null || incumbentNode is null)
        {
            const string message = "No integer-feasible solution was found in the branch and bound tree.";
            log.Note(message, SnapshotHighlight.DeadEnd);
            return SolutionResult.Failure(SolutionStatus.Infeasible, AlgorithmName, message, log);
        }

        string bestDescription = Describe(incumbentNode);
        log.Note($"Best candidate is Table {incumbentNode.Label} ({bestDescription}) with z = {Fmt(incumbent.ObjectiveValue)}.");

        return new SolutionResult
        {
            Status = SolutionStatus.Optimal,
            AlgorithmName = AlgorithmName,
            ObjectiveValue = incumbent.ObjectiveValue,
            VariableValues = incumbent.VariableValues,
            Log = log,
            FinalTableau = incumbent.FinalTableau,
            SourceModel = incumbentNode.Model,
            Message = $"Best candidate is Table {incumbentNode.Label} ({bestDescription})."
        };
    }

    /// <summary>Max: candidate strictly greater. Min: candidate strictly less.</summary>
    private static bool Improves(LPModel model, double candidateZ, double incumbentZ)
        => model.Objective == ObjectiveType.Max
            ? candidateZ > incumbentZ + IntegerEps
            : candidateZ < incumbentZ - IntegerEps;

    /// <summary>Lowest-index integer-restricted variable that is not (close to) integral, or -1.</summary>
    private static int FirstFractionalIndex(LPModel model, double[] values)
    {
        foreach (int i in model.IntegerVariableIndices)
        {
            double v = values[i];
            if (Math.Abs(v - Math.Round(v)) > IntegerEps) return i;
        }
        return -1;
    }

    /// <summary>Cumulative branching constraints from the root down to (and excluding) the root itself.</summary>
    private static string Describe(BranchNode node)
    {
        var parts = new List<string>();
        for (var n = node; n.Parent is not null; n = n.Parent) parts.Add(n.BranchDescription);
        parts.Reverse();
        return string.Join(", ", parts);
    }

    private static string VariablesLine(LPModel model, SolutionResult result)
    {
        var parts = Enumerable.Range(0, result.VariableValues.Length)
            .Select(i => $"{model.VariableNames.ElementAtOrDefault(i) ?? $"x{i + 1}"} = {Fmt(result.VariableValues[i])}");
        return "x variables = " + string.Join(", ", parts);
    }

    /// <summary>
    /// Folds one node's PrimalSimplex sub-log into the driver's log: the canonical form gets a
    /// custom "Table {label}  {description}  [Canonical Form]" header, every other snapshot is
    /// re-labelled "Table {label} - {original label}", and the footer (if any) is attached to
    /// the last snapshot so it prints directly under that node's closing tableau.
    /// </summary>
    private static void AppendNodeLog(IterationLog log, BranchNode node, SolutionResult result, string description,
                                      string? footer, SnapshotHighlight highlight)
    {
        var snapshots = result.Log.Snapshots;
        for (int i = 0; i < snapshots.Count; i++)
        {
            var s = snapshots[i];
            bool isFirst = i == 0;
            bool isLast = i == snapshots.Count - 1;

            log.Add(new TableauSnapshot
            {
                Label = isFirst
                    ? $"Table {node.Label}   {description}   [Canonical Form]"
                    : $"Table {node.Label} - {s.Label}",
                Snapshot = s.Snapshot,
                Pivot = s.Pivot,
                Note = s.Note,
                Footer = isLast ? footer : null,
                // The sub-solve's own snapshots are colored from the LP relaxation's point of view
                // (its "Optimal Tableau" would show green). Overriding the closing snapshot here
                // reflects what that optimum means for the B&B tree as a whole - e.g. fractional-but-
                // optimal is still "not yet complete" (yellow) at this level, not "done" (green).
                Highlight = isLast ? highlight : s.Highlight
            });
        }

        foreach (var n in result.Log.Notes)
            log.Note($"Table {node.Label}: {n}");
    }

    private static string Fmt(double v)
    {
        double r = Math.Round(v, 3);
        if (Math.Abs(r) < 1e-9) r = 0;
        return r.ToString("0.###");
    }
}

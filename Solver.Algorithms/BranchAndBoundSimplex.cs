using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Solver.Core;
using Solver.Core.Interfaces;
using Solver.Core.Models;
using Solver.Core.Results;

namespace Solver.Algorithms;


public sealed class BranchAndBoundSimplex : ISolver
{
    /// Safety valve so a bad model cannot spin forever.
    public const int MaxNodes = 400;

    private const double IntegerTolerance = 1e-6; //intiger variables are considered integral if they are within this distance of a whole number

    private const double BoundTolerance = 1e-9;

    public string AlgorithmName => "Branch and Bound (Simplex)";

    public bool CanSolve(LPModel model) => model.HasIntegerRestrictions;

    public SolutionResult Solve(LPModel model) //solves the model using branch and bound with simplex relaxation
    {
        var log = new IterationLog();
        bool maximise = model.Objective == ObjectiveType.Max;

        var stack = new Stack<BranchNode>();
        stack.Push(BranchNode.Root(model));

        BranchNode? best = null;
        double bestZ = maximise ? double.NegativeInfinity : double.PositiveInfinity;
        int candidates = 0;
        int explored = 0;
        bool hitCap = false;

        while (stack.Count > 0)
        {
            if (explored >= MaxNodes)
            {
                hitCap = true;
                log.Note($"Stopped after {MaxNodes} tables with {stack.Count} still unexplored.");
                break;
            }

            var node = stack.Pop();
            explored++;

            var relaxation = new PrimalSimplex().Solve(node.Model);
            node.Relaxation = relaxation;

            // unbounded relaxation kills the whole problem, not just this branch
            if (relaxation.Status == SolutionStatus.Unbounded)
            {
                node.Outcome = "UNBOUNDED";
                Emit(log, node, relaxation, Footer(model, relaxation, "UNBOUNDED - the relaxation has no finite optimum."));
                return SolutionResult.Failure(SolutionStatus.Unbounded, AlgorithmName,
                    $"Table {node.Label} is unbounded, so the integer problem is unbounded too.", log);
            }

            // infeasible or stalled: close the branch
            if (!relaxation.IsOptimal)
            {
                string closed = relaxation.Status == SolutionStatus.Infeasible
                    ? "INF - no feasible solution down this branch, so it is closed."
                    : $"{relaxation.Status} - {relaxation.Message}";

                node.Outcome = closed;
                Emit(log, node, relaxation, Footer(model, relaxation, closed));
                continue;
            }

            double z = relaxation.ObjectiveValue;
            int fractional = FirstFractionalVariable(model, relaxation.VariableValues);

            // every integer variable is integral: this table is a candidate
            if (fractional < 0)
            {
                candidates++;
                bool better = maximise ? z > bestZ + BoundTolerance : z < bestZ - BoundTolerance;

                string verdict;
                if (better)
                {
                    best = node;
                    bestZ = z;
                    verdict = $"Candidate {candidates} - best so far, z = {Round(z)}.";
                }
                else
                {
                    verdict = $"Candidate {candidates} - z = {Round(z)} does not beat the incumbent " +
                              $"z = {Round(bestZ)}, so it is discarded.";
                }

                node.Outcome = verdict;
                Emit(log, node, relaxation, Footer(model, relaxation, verdict));
                continue;
            }

            //fractional, but the bound says this branch cannot win
            if (best is not null && !CanBeat(z, bestZ, maximise))
            {
                string pruned = $"Pruned - bound z = {Round(z)} cannot beat the incumbent z = {Round(bestZ)}.";
                node.Outcome = pruned;
                Emit(log, node, relaxation, Footer(model, relaxation, pruned));
                continue;
            }

            // fractional: branch
            string branchName = VariableName(model, fractional);
            double value = relaxation.VariableValues[fractional];
            bool binary = model.SignRestrictions[fractional] == SignRestriction.Bin;

            string branching = binary
                ? $"branching {branchName} further ({branchName}=0 first, then {branchName}=1)"
                : $"branching {branchName} further ({branchName} <= {Math.Floor(value):0.###} first, " +
                  $"then {branchName} >= {Math.Ceiling(value):0.###})";

            node.Outcome = branching;
            Emit(log, node, relaxation, Footer(model, relaxation, branching));

            var lower = node.Branch(fractional, RelationType.LessEqual, Math.Floor(value), 1,
                binary ? $"{branchName}=0" : null);
            var upper = node.Branch(fractional, RelationType.GreaterEqual, Math.Ceiling(value), 2,
                binary ? $"{branchName}=1" : null);

            // Pushed upper first so the lower ("=0") child is popped and solved first.
            stack.Push(upper);
            stack.Push(lower);
        }


        if (best is null) // no candidate was ever found
        {
            log.Note($"Explored {explored} table(s). No integer feasible solution was found.");
            return SolutionResult.Failure(SolutionStatus.Infeasible, AlgorithmName,
                "Every branch closed without an integer feasible solution.", log);
        }

        var winner = best.Relaxation!;
        log.Note($"Explored {explored} table(s) and found {candidates} candidate(s).");
        log.Note($"Best candidate is Table {best.Label} ({BranchPath(best, "no branches")}) with z = {Round(bestZ)}.");

        return new SolutionResult 
        {
            Status = hitCap ? SolutionStatus.SuboptimalIncumbent : SolutionStatus.Optimal,
            AlgorithmName = AlgorithmName,
            ObjectiveValue = winner.ObjectiveValue,
            VariableValues = SnapIntegers(model, winner.VariableValues),
            Log = log,
            FinalTableau = winner.FinalTableau,
            SourceModel = best.Model,
            Message = hitCap
                ? $"Node cap reached - best candidate so far is Table {best.Label}."
                : $"Best candidate is Table {best.Label} ({BranchPath(best, "no branches")})."
        };
    }


    private static void Emit(IterationLog log, BranchNode node, SolutionResult relaxation, string footer) // copies the tableau snapshots from the relaxation into the master log, with the node's label and a footer
    {
        var snapshots = relaxation.Log.Snapshots;
        string header = $"Table {node.Label}   {BranchPath(node, "root LP relaxation")}";

        if (snapshots.Count == 0)
        {
            // Nothing to show the model was rejected before a tableau was ever built.
            log.Note($"{header} - {relaxation.Message}");
            return;
        }

        for (int i = 0; i < snapshots.Count; i++)
        {
            var s = snapshots[i];
            log.Add(new TableauSnapshot
            {
                Label = i == 0 ? $"{header}   [{s.Label}]" : $"Table {node.Label} - {s.Label}",
                Snapshot = s.Snapshot,
                Pivot = s.Pivot,
                Note = s.Note,
                Footer = i == snapshots.Count - 1 ? footer : null
            });
        }

        foreach (var note in relaxation.Log.Notes)
            log.Note($"Table {node.Label}: {note}");
    }

    /// The three summary lines printed under a node's last tableau
    private static string Footer(LPModel model, SolutionResult relaxation, string verdict)
    {
        var sb = new StringBuilder();
        sb.AppendLine(new string('-', 58));

        if (relaxation.IsOptimal)
        {
            sb.AppendLine("  x variables = " + string.Join(", ",
                relaxation.VariableValues.Select((v, i) => $"{VariableName(model, i)} = {Round(v)}")));
            sb.AppendLine($"  z value     = {Round(relaxation.ObjectiveValue)}");
        }
        else
        {
            sb.AppendLine("  x variables = (none - this table has no feasible solution)");
            sb.AppendLine("  z value     = (none)");
        }

        sb.AppendLine($"  This table is: {verdict}");
        return sb.ToString();
    }

    private static string BranchPath(BranchNode node, string whenRoot) //complete path from the root to this node
    {
        var parts = new List<string>();
        for (var n = node; n?.Parent is not null; n = n.Parent)
            parts.Add(n.BranchDescription);

        if (parts.Count == 0) return whenRoot;

        parts.Reverse();
        return string.Join(", ", parts);
    }

    private static int FirstFractionalVariable(LPModel model, double[] values) //loops through the integer variable indices and checks if they are integral, returns the first index that is not integral, or -1 if all are integral
    {
        foreach (int j in model.IntegerVariableIndices)
        {
            if (j >= values.Length) continue;
            if (!IsIntegral(values[j])) return j;
        }
        return -1;
    }

    private static bool IsIntegral(double v) => Math.Abs(v - Math.Round(v)) <= IntegerTolerance;

    /// Can a relaxation bound of z still improve on the incumbent?
    private static bool CanBeat(double z, double incumbent, bool maximise)
        => maximise ? z > incumbent + BoundTolerance : z < incumbent - BoundTolerance;

    private static double[] SnapIntegers(LPModel model, double[] values) //returns a copy of the values array with integer variables rounded to the nearest whole number, and any value close to zero set to 0.0
    {
        var snapped = (double[])values.Clone();

        foreach (int j in model.IntegerVariableIndices)
            if (j < snapped.Length) snapped[j] = Math.Round(snapped[j]);

        for (int j = 0; j < snapped.Length; j++)
            if (Math.Abs(snapped[j]) < BoundTolerance) snapped[j] = 0.0;   // also kills "-0"

        return snapped;
    }

    private static string VariableName(LPModel model, int index)
        => model.VariableNames.ElementAtOrDefault(index) ?? $"x{index + 1}";

    private static string Round(double v)
    {
        if (double.IsInfinity(v)) return "none";
        double r = Math.Round(v, 3);
        if (Math.Abs(r) < BoundTolerance) r = 0.0;   // never print "-0"
        return $"{r:0.###}";
    }
}

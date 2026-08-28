namespace Solver.Core;

/// <summary>
/// Semantic status of one printed iteration/node/note, used only to pick a console color.
/// Set by whichever algorithm produced the line; TableauFormatter maps it to a ConsoleColor.
/// Never affects the plain-text output file - OutputFileWriter ignores it entirely.
/// </summary>
public enum SnapshotHighlight
{
    /// <summary>Still working: an intermediate pivot, or a node whose relaxation solved but
    /// isn't integral yet and must branch/cut further. Not the final word on this line. Yellow.</summary>
    InProgress,

    /// <summary>A terminal, "cannot go any further" result: the final optimal tableau, or a
    /// node that becomes the new best candidate. Green.</summary>
    Best,

    /// <summary>A terminal candidate that did not beat the incumbent. Blue.</summary>
    Candidate,

    /// <summary>A dead end: infeasible, unbounded, or pruned by bound. Red.</summary>
    DeadEnd
}

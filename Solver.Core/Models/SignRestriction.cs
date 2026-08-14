namespace Solver.Core.Models;

/// <summary>
/// Per-variable restriction from the final line of the input file.
/// +   -> Positive  (x >= 0)
/// -   -> Negative  (x less than or equal to 0)
/// urs -> Urs       (unrestricted in sign)
/// int -> Int       (x >= 0 and integer)
/// bin -> Bin       (x in {0,1})
/// </summary>
public enum SignRestriction
{
    Positive,
    Negative,
    Urs,
    Int,
    Bin
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Solver.Core.Models;

namespace Solver.Core.IO;

/// <summary>
/// Parses the module's input file format.
///
///   line 1   : max|min then one signed coefficient per decision variable
///   line 2..k: one signed coefficient per variable, then &lt;= | &gt;= | = , then the RHS
///   last line: one sign restriction per variable: + | - | urs | int | bin
///
/// Example:
///   max +2 +3 +3 +5 +2 +4
///   +11 +8 +6 +14 +10 +10 &lt;=40
///   bin bin bin bin bin bin
/// </summary>
public static class InputFileParser
{
    private static readonly string[] Relations = { "<=", ">=", "=" };

    public static LPModel ParseFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Input file not found: {path}");
        return Parse(File.ReadAllLines(path));
    }

    public static LPModel Parse(IEnumerable<string> rawLines)
    {
        var lines = rawLines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("//") && !l.StartsWith("#"))
            .ToList();

        if (lines.Count < 3)
            throw new FormatException("Input needs at least an objective line, one constraint and a sign-restriction line.");

        // ---- objective line ----
        var objTokens = Tokenise(lines[0]);
        var direction = objTokens[0].ToLowerInvariant() switch
        {
            "max" or "maximise" or "maximize" => ObjectiveType.Max,
            "min" or "minimise" or "minimize" => ObjectiveType.Min,
            _ => throw new FormatException($"Line 1 must start with max or min, found '{objTokens[0]}'.")
        };

        var objCoefficients = objTokens.Skip(1).Select(ParseNumber).ToArray();
        if (objCoefficients.Length == 0)
            throw new FormatException("Objective line has no coefficients.");

        int n = objCoefficients.Length;

        // ---- sign restriction line (always last) ----
        var signTokens = Tokenise(lines[^1]);
        if (signTokens.Length != n)
            throw new FormatException(
                $"Sign-restriction line has {signTokens.Length} entries but the objective has {n} variables.");

        var signs = signTokens.Select(ParseSign).ToArray();

        // ---- constraints ----
        var constraints = new List<Constraint>();
        for (int i = 1; i < lines.Count - 1; i++)
        {
            var tokens = Tokenise(lines[i]);
            int relIdx = Array.FindIndex(tokens, t => Relations.Contains(t));
            if (relIdx < 0)
                throw new FormatException($"Constraint on line {i + 1} has no <=, >= or = operator.");

            var coefficients = tokens.Take(relIdx).Select(ParseNumber).ToArray();
            if (coefficients.Length != n)
                throw new FormatException(
                    $"Constraint on line {i + 1} has {coefficients.Length} coefficients but the objective has {n} variables.");

            var relation = tokens[relIdx] switch
            {
                "<=" => RelationType.LessEqual,
                ">=" => RelationType.GreaterEqual,
                _ => RelationType.Equal
            };

            var rhsTokens = tokens.Skip(relIdx + 1).ToArray();
            if (rhsTokens.Length != 1)
                throw new FormatException($"Constraint on line {i + 1} must have exactly one RHS value.");

            constraints.Add(new Constraint
            {
                Coefficients = coefficients,
                Relation = relation,
                Rhs = ParseNumber(rhsTokens[0]),
                Name = $"c{constraints.Count + 1}"
            });
        }

        if (constraints.Count == 0)
            throw new FormatException("No constraints found.");

        return new LPModel
        {
            Objective = direction,
            ObjectiveCoefficients = objCoefficients,
            Constraints = constraints,
            SignRestrictions = signs,
            VariableNames = Enumerable.Range(1, n).Select(i => $"x{i}").ToArray()
        };
    }

    /// <summary>
    /// Splits on whitespace but also detaches a relation glued to its RHS, e.g. "&lt;=40" -> "&lt;=", "40".
    /// </summary>
    private static string[] Tokenise(string line)
    {
        var result = new List<string>();
        foreach (var raw in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw;
            var match = Relations.FirstOrDefault(r => token.StartsWith(r, StringComparison.Ordinal));
            if (match is not null && token.Length > match.Length)
            {
                result.Add(match);
                result.Add(token[match.Length..]);
            }
            else
            {
                result.Add(token);
            }
        }
        return result.ToArray();
    }

    private static double ParseNumber(string token)
    {
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
        throw new FormatException($"'{token}' is not a valid number.");
    }

    private static SignRestriction ParseSign(string token) => token.ToLowerInvariant() switch
    {
        "+" or "pos" or "positive" => SignRestriction.Positive,
        "-" or "neg" or "negative" => SignRestriction.Negative,
        "urs" => SignRestriction.Urs,
        "int" or "integer" => SignRestriction.Int,
        "bin" or "binary" => SignRestriction.Bin,
        _ => throw new FormatException($"Unknown sign restriction '{token}'. Expected + - urs int bin.")
    };
}

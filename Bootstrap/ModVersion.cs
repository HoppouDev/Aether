using System.Text.RegularExpressions;

namespace Aether.Bootstrap;

internal sealed record ModVersion(string[] Core, string[]? Prerelease) : IComparable<ModVersion>
{
    private const string CorePattern = @"(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*))*";
    private const string IdentifierPattern = @"[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*";
    private static readonly Regex VersionPattern = new(
        $@"\A(?<core>{CorePattern})(?:-(?<pre>{IdentifierPattern}))?(?:\+{IdentifierPattern})?\z");
    private static readonly Regex WildcardPattern = new($@"\A(?<prefix>{CorePattern})(?:\.[xX*])+\z");

    internal static ModVersion? Parse(string text, bool allowPrereleaseBoundary = false)
    {
        bool boundary = allowPrereleaseBoundary && text.EndsWith('-') && !text[..^1].Contains('-') && !text.Contains('+');
        Match match = VersionPattern.Match(boundary ? text[..^1] : text);
        if (!match.Success)
            return null;

        string[]? prerelease = boundary ? [] : match.Groups["pre"].Success ? match.Groups["pre"].Value.Split('.') : null;
        if (prerelease != null && prerelease.Any(part => part.Length > 1 && part[0] == '0' && IsNumeric(part)))
            return null;

        return new ModVersion(match.Groups["core"].Value.Split('.'), prerelease);
    }

    private static bool IsNumeric(string value) => value.All(char.IsAsciiDigit);

    private static int CompareNumbers(string first, string second) =>
        first.Length == second.Length ? string.CompareOrdinal(first, second) : first.Length.CompareTo(second.Length);

    private string Component(int index) => index < Core.Length ? Core[index] : "0";

    public int CompareTo(ModVersion? other)
    {
        if (other == null)
            return 1;

        for (int index = 0; index < Math.Max(Core.Length, other.Core.Length); index++)
        {
            int comparison = CompareNumbers(Component(index), other.Component(index));
            if (comparison != 0)
                return comparison;
        }

        if (Prerelease == null || other.Prerelease == null)
            return (Prerelease == null).CompareTo(other.Prerelease == null);

        for (int index = 0; index < Math.Min(Prerelease.Length, other.Prerelease.Length); index++)
        {
            string first = Prerelease[index], second = other.Prerelease[index];
            bool firstNumeric = IsNumeric(first), secondNumeric = IsNumeric(second);
            int comparison = firstNumeric && secondNumeric ? CompareNumbers(first, second)
                : firstNumeric != secondNumeric ? secondNumeric.CompareTo(firstNumeric)
                : string.CompareOrdinal(first, second);
            if (comparison != 0)
                return comparison;
        }
        return Prerelease.Length.CompareTo(other.Prerelease.Length);
    }

    internal static Func<ModVersion?, bool> ParseConstraint(string constraint)
    {
        string[] terms = constraint.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0)
            throw new FormatException("A version constraint cannot be empty.");

        var predicates = terms.Select(ParseTerm).ToArray();
        return version => predicates.All(predicate => predicate(version));
    }

    private static Func<ModVersion?, bool> ParseTerm(string term)
    {
        if (term is "*" or "x" or "X")
            return _ => true;

        Match wildcard = WildcardPattern.Match(term);
        if (wildcard.Success)
        {
            string[] prefix = wildcard.Groups["prefix"].Value.Split('.');
            return version => version != null && HasPrefix(version, prefix, prefix.Length);
        }

        string op = term.StartsWith(">=") || term.StartsWith("<=") ? term[..2]
            : term[0] is '>' or '<' or '=' or '~' or '^' ? term[..1] : "";
        ModVersion bound = Parse(term[op.Length..], allowPrereleaseBoundary: true)
            ?? throw new FormatException($"Invalid version term '{term}'.");

        return version => version != null && Matches(version, op, bound);
    }

    private static bool HasPrefix(ModVersion version, string[] prefix, int length)
    {
        for (int index = 0; index < length; index++)
            if (version.Component(index) != prefix[index])
                return false;
        return true;
    }

    private static bool Matches(ModVersion version, string op, ModVersion bound)
    {
        int comparison = version.CompareTo(bound);
        return op switch
        {
            ">" => comparison > 0,
            ">=" => comparison >= 0,
            "<" => comparison < 0,
            "<=" => comparison <= 0,
            "~" => comparison >= 0 && HasPrefix(version, bound.Core, Math.Min(2, bound.Core.Length)),
            "^" => comparison >= 0 && HasPrefix(version, bound.Core, 1),
            _ => comparison == 0
        };
    }
}

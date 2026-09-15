using System.Text.RegularExpressions;

namespace LineEndingNormalizer;

/// <summary>
/// Matches wildcard filenames and relative paths.
/// Patterns without a separator match filenames; patterns with a separator
/// match paths relative to the scan root. '\' is treated as '/'.
/// </summary>
internal static partial class FilePatternMatcher
{
    [GeneratedRegex(
        "^.*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex MatchAllRegex();

    /// <summary>
    /// Returns true when any pattern matches the filename/path.
    /// Supports '*' and '?' using a case-insensitive comparison.
    /// </summary>
    public static bool IsMatch(
        string fileName,
        IEnumerable<Regex> patterns)
    {
        foreach (Regex pattern in patterns)
        {
            if (pattern.IsMatch(fileName))
            {
                return true;
            }
        }

        return false;
    }


    /// <summary>
    /// Converts wildcard patterns to compiled regexes.
    /// </summary>
    public static List<Regex> Compile(
        List<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        var result =
            new List<Regex>(patterns.Count);

        foreach (string pattern in patterns)
        {
            string fileMask =
                pattern.Trim();

            if (fileMask.Length == 0)
            {
                continue;
            }

            // A separator-free mask must match the filename at any depth, not
            // just at the scan root, so it needs an optional directory prefix.
            bool hasSeparator =
                fileMask.Contains('/') ||
                fileMask.Contains('\\');

            string body =
                Regex.Escape(fileMask.Replace('\\', '/'))
                    .Replace(@"\*", ".*")
                    .Replace(@"\?", ".");

            string anchored =
                hasSeparator
                    ? "^" + body + "$"
                    : "^(?:.*/)?" + body + "$";

            result.Add(
                new Regex(
                    anchored,
                    RegexOptions.IgnoreCase |
                    RegexOptions.CultureInvariant |
                    RegexOptions.Compiled));
        }

        // Empty input means match everything.
        if (result.Count == 0)
        {
            result.Add(MatchAllRegex());
        }

        return result;
    }
}

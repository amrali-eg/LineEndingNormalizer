using System.Text.RegularExpressions;

namespace LineEndingNormalizer;

/// <summary>
/// Recursively enumerates files while applying directory and file exclusions.
/// </summary>
internal static class DirectoryTraversal
{
    /// <summary>
    /// Directories excluded from recursive scanning.
    /// </summary>
    private static readonly HashSet<string> DefaultExcludedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".svn",
            ".hg",
            ".vs",
            ".idea",
            "bin",
            "obj",
            "node_modules",
            "packages",
            "dist",
            "build",
            "target"
        };

    /// <summary>
    /// Enumerates files while skipping excluded directories and reparse points.
    /// Enumeration failures are reported through <paramref name="onWarning"/>.
    /// </summary>
    internal static IEnumerable<string> EnumerateCandidateFiles(
        string basePath,
        Action<string>? onWarning = null,
        string? excludedFullPath = null)
    {
        var pending =
            new Stack<string>();

        pending.Push(basePath);

        while (pending.Count > 0)
        {
            string dir =
                pending.Pop();

            List<string>? subDirectories =
                TryEnumerate(
                    dir,
                    Directory.EnumerateDirectories,
                    onWarning);

            if (subDirectories != null)
            {
                foreach (string subDirectory in subDirectories)
                {
                    if (DefaultExcludedDirectoryNames.Contains(
                            Path.GetFileName(subDirectory)))
                    {
                        continue;
                    }

                    if (IsReparsePointDirectory(subDirectory))
                    {
                        onWarning?.Invoke(
                            string.Format(
                                "Skipping directory (symlink/junction): {0}",
                                subDirectory));

                        continue;
                    }

                    pending.Push(subDirectory);
                }
            }

            List<string>? files =
                TryEnumerate(
                    dir,
                    Directory.EnumerateFiles,
                    onWarning);

            if (files != null)
            {
                foreach (string file in files)
                {
                    if (excludedFullPath is not null &&
                        string.Equals(
                            file,
                            excludedFullPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (ShouldSkipFile(
                            file,
                            out string? reason))
                    {
                        if (reason != null)
                        {
                            onWarning?.Invoke(reason);
                        }

                        continue;
                    }

                    yield return file;
                }
            }
        }
    }


    /// <summary>
    /// Rejects file reparse points before any mode can open and follow them.
    /// Attribute failures are also skipped because LEN cannot prove the path
    /// is a regular file inside the requested tree.
    /// </summary>
    internal static bool ShouldSkipFile(
        string file,
        out string? reason)
    {
        try
        {
            if ((File.GetAttributes(file) &
                 FileAttributes.ReparsePoint) != 0)
            {
                reason =
                    "Skipping file (symlink/reparse point): " + file;

                return true;
            }

            reason = null;
            return false;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException)
        {
            reason =
                "Skipping file (cannot inspect): " + file +
                Environment.NewLine +
                "    " + ex.Message;

            return true;
        }
    }


    /// <summary>
    /// Returns true for reparse-point directories or when attributes cannot be read.
    /// </summary>
    internal static bool IsReparsePointDirectory(
        string dir)
    {
        try
        {
            return
                (File.GetAttributes(dir) &
                 FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException)
        {
            return true;
        }
    }


    /// <summary>
    /// Enumerates a directory and reports recoverable access or I/O failures.
    /// </summary>
    internal static List<string>? TryEnumerate(
        string dir,
        Func<string, IEnumerable<string>> enumerate,
        Action<string>? onWarning = null)
    {
        try
        {
            return [.. enumerate(dir)];
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or
            IOException)
        {
            onWarning?.Invoke(
                string.Format(
                    "Skipping directory (cannot list): {0}{1}    {2}",
                    dir,
                    Environment.NewLine,
                    ex.Message));

            return null;
        }
    }


    /// <summary>
    /// Applies the include/exclude rules and always rejects tool-generated files.
    /// </summary>
    /// <remarks>
    /// <paramref name="fileName"/> may be a bare filename or a path relative
    /// to the scan root -- whichever FilePatternMatcher's compiled patterns
    /// expect for a given call site.
    /// </remarks>
    internal static bool IsCandidateFile(
        string fileName,
        List<Regex> includePatterns,
        List<Regex>? excludePatterns)
    {
        // Never rescan the tool's own backup output.
        if (fileName.EndsWith(
                ".bak",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Never rescan abandoned conversion temp files.
        if (fileName.EndsWith(
                "." + LosslessFileWriter.TempFileSuffix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!FilePatternMatcher.IsMatch(
                fileName,
                includePatterns))
        {
            return false;
        }

        if (excludePatterns != null &&
            FilePatternMatcher.IsMatch(
                fileName,
                excludePatterns))
        {
            return false;
        }

        return true;
    }
}

namespace LineEndingNormalizer.Tests;

/// <summary>
/// Coverage for DirectoryTraversal.EnumerateCandidateFiles: default directory
/// exclusion and resilience to a directory that can't be listed.
/// </summary>
public sealed class DirectoryWalkTests
{
    [Fact]
    public void DefaultExcludedDirectories_AreNeverDescendedInto()
    {
        using var dir = new TempDirectory();

        dir.WriteFile("keep/a.txt", [.. "a"u8]);
        dir.WriteFile(".git/HEAD", [.. "ref"u8]);
        dir.WriteFile(".git/objects/pack/x.pack", [.. "x"u8]);
        dir.WriteFile("node_modules/pkg/index.js", [.. "x"u8]);
        dir.WriteFile("bin/Debug/out.dll", [.. "x"u8]);
        dir.WriteFile("obj/Debug/tmp.txt", [.. "x"u8]);
        dir.WriteFile(".vs/settings.txt", [.. "x"u8]);
        dir.WriteFile("dist/bundle.js", [.. "x"u8]);

        // Not excluded: only the exact name ".git" is special-cased, so a
        // lookalike directory like ".github" must still be walked.
        dir.WriteFile(".github/workflows/ci.yml", [.. "x"u8]);

        var found = DirectoryTraversal.EnumerateCandidateFiles(dir.Path)
            .Select(f => Path.GetRelativePath(dir.Path, f).Replace('\\', '/'))
            .ToHashSet();

        Assert.Contains("keep/a.txt", found);
        Assert.Contains(".github/workflows/ci.yml", found);

        Assert.DoesNotContain(found, f => f.StartsWith(".git/"));
        Assert.DoesNotContain(found, f => f.StartsWith("node_modules/"));
        Assert.DoesNotContain(found, f => f.StartsWith("bin/"));
        Assert.DoesNotContain(found, f => f.StartsWith("obj/"));
        Assert.DoesNotContain(found, f => f.StartsWith(".vs/"));
        Assert.DoesNotContain(found, f => f.StartsWith("dist/"));
    }

    [Fact]
    public void UnlistableRoot_ReturnsEmpty_RatherThanThrowing()
    {
        string ghostRoot = Path.Combine(
            Path.GetTempPath(),
            "len-tests-ghost-" + Guid.NewGuid().ToString("N"));

        // Never created, so listing it fails with DirectoryNotFoundException.
        var found = DirectoryTraversal.EnumerateCandidateFiles(ghostRoot).ToList();

        Assert.Empty(found);
    }

    [Fact]
    public void TryEnumerate_SwallowsDirectoryNotFound_AndReturnsNull()
    {
        string ghost = Path.Combine(
            Path.GetTempPath(),
            "len-tests-ghost-" + Guid.NewGuid().ToString("N"));

        List<string>? result =
            DirectoryTraversal.TryEnumerate(ghost, Directory.EnumerateFiles);

        Assert.Null(result);
    }

    [Fact]
    public void TryEnumerate_ReturnsRealListing_ForAnAccessibleDirectory()
    {
        using var dir = new TempDirectory();

        dir.WriteFile("a.txt", [.. "x"u8]);
        dir.WriteFile("b.txt", [.. "x"u8]);

        List<string>? result =
            DirectoryTraversal.TryEnumerate(dir.Path, Directory.EnumerateFiles);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    /// <summary>
    /// An unreadable directory used to leave a warning on stderr and nothing
    /// else: no counter, exit 0. A run that never saw part of the tree could
    /// report success. The counter closes that gap.
    /// </summary>
    [Fact]
    public void UnlistableRoot_IsCountedAsDirectoriesUnreadable()
    {
        string ghostRoot = Path.Combine(
            Path.GetTempPath(),
            "len-tests-ghost-" + Guid.NewGuid().ToString("N"));

        var statistics = new Statistics();

        var found = DirectoryTraversal.EnumerateCandidateFiles(
            ghostRoot,
            statistics: statistics).ToList();

        // The walk tries to list subdirectories and files independently for
        // each directory, so a root that does not exist fails both listings.
        Assert.Empty(found);
        Assert.Equal(2, statistics.DirectoriesUnreadable);
    }

    [Fact]
    public void TryEnumerate_CountsDirectoriesUnreadable_OnFailure()
    {
        string ghost = Path.Combine(
            Path.GetTempPath(),
            "len-tests-ghost-" + Guid.NewGuid().ToString("N"));

        var statistics = new Statistics();

        List<string>? result = DirectoryTraversal.TryEnumerate(
            ghost,
            Directory.EnumerateFiles,
            statistics: statistics);

        Assert.Null(result);
        Assert.Equal(1, statistics.DirectoriesUnreadable);
    }

    /// <summary>
    /// A folder skipped by reserved name (.git, bin, obj, ...) was silent:
    /// its contents were never counted anywhere. This keeps the exclusion
    /// visible without changing that it is skipped.
    /// </summary>
    [Fact]
    public void ReservedNameDirectory_IsCountedAsDirectoriesSkippedByName()
    {
        using var dir = new TempDirectory();

        dir.WriteFile("keep/a.txt", [.. "a"u8]);
        dir.WriteFile(".git/HEAD", [.. "ref"u8]);
        dir.WriteFile("bin/Debug/out.dll", [.. "x"u8]);

        var statistics = new Statistics();

        var found = DirectoryTraversal.EnumerateCandidateFiles(
            dir.Path,
            statistics: statistics).ToList();

        Assert.Single(found);
        Assert.Equal(2, statistics.DirectoriesSkippedByName);
    }

    [Fact]
    public void AccessibleTreeWithNoExclusions_ReportsZeroForBothCounters()
    {
        using var dir = new TempDirectory();

        dir.WriteFile("a.txt", [.. "x"u8]);

        var statistics = new Statistics();

        DirectoryTraversal.EnumerateCandidateFiles(
            dir.Path,
            statistics: statistics).ToList();

        Assert.Equal(0, statistics.DirectoriesUnreadable);
        Assert.Equal(0, statistics.DirectoriesSkippedByName);
    }
}

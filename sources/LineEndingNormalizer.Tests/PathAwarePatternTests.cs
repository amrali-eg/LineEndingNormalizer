using System.Text.RegularExpressions;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// -Include/-Exclude patterns: a mask with no separator matches the bare filename at any
/// depth (backward compatible); one containing a separator matches the path relative to
/// the scan root instead, with "/" and "\" treated identically.
/// </summary>
public sealed class PathAwarePatternTests
{
    private static TempDirectory BuildTree()
    {
        var dir = new TempDirectory();

        dir.WriteFile("root.cs", "x"u8.ToArray());
        dir.WriteFile("src/a.cs", "x"u8.ToArray());
        dir.WriteFile("src/nested/b.cs", "x"u8.ToArray());
        dir.WriteFile("other/c.cs", "x"u8.ToArray());

        return dir;
    }

    private static HashSet<string> Scan(TempDirectory dir, string includePattern)
    {
        List<Regex> includePatterns = FilePatternMatcher.Compile([includePattern]);

        return
        [
            .. DirectoryTraversal.EnumerateCandidateFiles(dir.Path)
                .Select(file => Path.GetRelativePath(dir.Path, file).Replace('\\', '/'))
                .Where(relativePath =>
                    DirectoryTraversal.IsCandidateFile(relativePath, includePatterns, null))
        ];
    }

    [Fact]
    public void SeparatorFreePattern_StillMatchesAtEveryDepth()
    {
        using TempDirectory dir = BuildTree();

        // Regression guard: this must keep matching every *.cs file regardless of depth,
        // exactly as it did before path-aware matching was introduced.
        HashSet<string> found = Scan(dir, "*.cs");

        Assert.Equal(
            new HashSet<string> { "root.cs", "src/a.cs", "src/nested/b.cs", "other/c.cs" },
            found);
    }

    [Fact]
    public void PathQualifiedPattern_MatchesOnlyTheIntendedSubtree()
    {
        using TempDirectory dir = BuildTree();

        HashSet<string> found = Scan(dir, "src/*.cs");

        Assert.Equal(new HashSet<string> { "src/a.cs", "src/nested/b.cs" }, found);
        Assert.DoesNotContain("root.cs", found);
        Assert.DoesNotContain("other/c.cs", found);
    }

    [Fact]
    public void PathQualifiedPattern_WithBackslash_MatchesIdenticallyToForwardSlash()
    {
        using TempDirectory dir = BuildTree();

        HashSet<string> withSlash = Scan(dir, "src/*.cs");
        HashSet<string> withBackslash = Scan(dir, @"src\*.cs");

        Assert.Equal(withSlash, withBackslash);
    }

    [Fact]
    public void SeparatorFreeExactFilename_MatchesAtAnyDepth()
    {
        using TempDirectory dir = BuildTree();
        dir.WriteFile("src/nested/b.cs", "x"u8.ToArray());

        // b.cs only exists at src/nested/b.cs; a separator-free mask must
        // still find it there, per the documented "matches the filename at
        // any depth" contract.
        HashSet<string> found = Scan(dir, "b.cs");

        Assert.Equal(new HashSet<string> { "src/nested/b.cs" }, found);
    }

    [Fact]
    public void SeparatorFreeExactFilename_ExcludesOnlyExactMatch_NotSimilarNames()
    {
        using var dir = new TempDirectory();

        dir.WriteFile("Properties/AssemblyInfo.cs", "x"u8.ToArray());
        dir.WriteFile("Properties/AssemblyInfoHelper.cs", "x"u8.ToArray());

        List<Regex> includePatterns = FilePatternMatcher.Compile(["*"]);
        List<Regex> excludePatterns = FilePatternMatcher.Compile(["AssemblyInfo.cs"]);

        var found = DirectoryTraversal.EnumerateCandidateFiles(dir.Path)
            .Select(file => Path.GetRelativePath(dir.Path, file).Replace('\\', '/'))
            .Where(relativePath =>
                DirectoryTraversal.IsCandidateFile(relativePath, includePatterns, excludePatterns))
            .ToHashSet();

        Assert.DoesNotContain("Properties/AssemblyInfo.cs", found);
        Assert.Contains("Properties/AssemblyInfoHelper.cs", found);
    }
}

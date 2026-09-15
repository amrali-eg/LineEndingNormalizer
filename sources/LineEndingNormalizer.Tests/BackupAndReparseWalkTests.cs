using System.Text.RegularExpressions;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// Coverage for the -Backup flag, the .bak default exclusion, and the
/// directory walker's refusal to descend into a symlinked/junctioned
/// subdirectory.
/// </summary>
public sealed class BackupAndReparseWalkTests
{
    [Fact]
    public void Backup_WritesByteIdenticalCopy_BeforeReplacing()
    {
        using var dir = new TempDirectory();

        byte[] source = "a\nb\nc\n"u8.ToArray();
        string path = dir.WriteFile("needs_convert.txt", source);

        NormalizeResult result =
            NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false, backup: true);

        Assert.Equal(NormalizeResult.Converted, result);
        Assert.Equal("a\r\nb\r\nc\r\n"u8.ToArray(), File.ReadAllBytes(path));

        string backupPath = path + ".bak";
        Assert.True(File.Exists(backupPath));
        Assert.Equal(source, File.ReadAllBytes(backupPath));
    }

    [Fact]
    public void NoBackupFlag_NoBackupFileIsCreated()
    {
        using var dir = new TempDirectory();

        byte[] source = "a\nb\n"u8.ToArray();
        string path = dir.WriteFile("needs_convert.txt", source);

        NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false, backup: false);

        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void ReadOnlyPreviousBackup_IsStillReplaced_AndKeepsItsProtection()
    {
        using var dir = new TempDirectory();

        byte[] source = "a\nb\n"u8.ToArray();
        string path = dir.WriteFile("f.txt", source);

        string backupPath = path + ".bak";
        File.WriteAllBytes(backupPath, "stale backup content"u8.ToArray());
        File.SetAttributes(backupPath, FileAttributes.ReadOnly);

        try
        {
            NormalizeResult result =
                NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false, backup: true);

            // A ReadOnly .bak used to make the backup install fail with
            // ERROR_ACCESS_DENIED, which aborted the whole conversion.
            Assert.Equal(NormalizeResult.Converted, result);
            Assert.Equal("a\r\nb\r\n"u8.ToArray(), File.ReadAllBytes(path));

            // The backup was genuinely refreshed from the pre-conversion original...
            Assert.Equal(source, File.ReadAllBytes(backupPath));

            // ...and the backup slot keeps the ReadOnly protection it had.
            Assert.Equal(
                FileAttributes.ReadOnly,
                File.GetAttributes(backupPath) & FileAttributes.ReadOnly);
        }
        finally
        {
            // Clean up so TempDirectory's recursive delete can remove it.
            File.SetAttributes(backupPath, FileAttributes.Normal);
        }
    }

    [Fact]
    public void RepeatedBackupRuns_DoNotChainIntoBakBakBak()
    {
        using var dir = new TempDirectory();

        string path = dir.WriteFile("f.txt", "a\nb\n"u8.ToArray());

        List<Regex> includePatterns = FilePatternMatcher.Compile(["*"]);

        // Simulate two successive end-to-end tool runs with -Backup and a
        // broad include pattern, filtering candidates through the real
        // DirectoryTraversal.IsCandidateFile the same way ConvertDirectory does.
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (string candidate in DirectoryTraversal.EnumerateCandidateFiles(dir.Path).ToList())
            {
                if (!DirectoryTraversal.IsCandidateFile(Path.GetFileName(candidate), includePatterns, null))
                {
                    continue;
                }

                NewLineNormalizer.NormalizeFile(candidate, LineEnding.Crlf, whatIf: false, backup: true);
            }
        }

        Assert.True(File.Exists(path + ".bak"));
        Assert.False(File.Exists(path + ".bak.bak"));
    }

    [Theory]
    [InlineData("file.txt.bak", false)]
    [InlineData("file.BAK", false)]
    [InlineData("file.txt", true)]
    [InlineData("file.bakup", true)]
    public void IsCandidateFile_ExcludesBakFilesRegardlessOfCase(string fileName, bool expectedCandidate)
    {
        List<Regex> includePatterns = FilePatternMatcher.Compile(["*"]);

        bool isCandidate = DirectoryTraversal.IsCandidateFile(fileName, includePatterns, null);

        Assert.Equal(expectedCandidate, isCandidate);
    }

    [Theory]
    [InlineData("file.txt.abc123.len.tmp", false)]
    [InlineData("file.txt.abc123.LEN.TMP", false)]
    [InlineData("file.txt.abc123.Len.Tmp", false)]
    [InlineData("file.txt", true)]
    [InlineData("file.txt.tmp", true)]        // plain .tmp is NOT excluded
    [InlineData("file.len.tmpx", true)]       // near-miss suffix is NOT excluded
    public void IsCandidateFile_ExcludesAbandonedTempFilesRegardlessOfCase(string fileName, bool expectedCandidate)
    {
        List<Regex> includePatterns = FilePatternMatcher.Compile(["*"]);

        bool isCandidate = DirectoryTraversal.IsCandidateFile(fileName, includePatterns, null);

        Assert.Equal(expectedCandidate, isCandidate);
    }

    [Theory]
    [InlineData("file.txt.abc123456789.bak.len.tmp", false)]
    [InlineData("file.txt.ABC123456789.BAK.LEN.TMP", false)]
    [InlineData("file.txt.bak", false)]
    [InlineData("file.txt.tmp", true)]
    [InlineData("file.txt", true)]
    public void IsCandidateFile_ExcludesBackupTempFilesRegardlessOfCase(string fileName, bool expectedCandidate)
    {
        List<Regex> includePatterns = FilePatternMatcher.Compile(["*"]);

        bool isCandidate = DirectoryTraversal.IsCandidateFile(fileName, includePatterns, null);

        Assert.Equal(expectedCandidate, isCandidate);
    }

    [Fact]
    public void AbandonedBackupTempFile_UsingRealNamingScheme_IsExcludedFromADirectoryScan()
    {
        using var dir = new TempDirectory();

        dir.WriteFile("f.txt", "a\nb\n"u8.ToArray());

        // Simulates a backup temp file left behind by a process killed
        // mid-CreateBackup, named exactly as CreateBackup names it
        // (<file>.<guid12>.bak.<TempFileSuffix>), not a hand-typed guess.
        dir.WriteFile("f.txt.abc123456789.bak.len.tmp", "old\n"u8.ToArray());

        List<Regex> includePatterns = FilePatternMatcher.Compile(["*"]);

        var found = DirectoryTraversal.EnumerateCandidateFiles(dir.Path)
            .Select(Path.GetFileName)
            .Where(fileName => DirectoryTraversal.IsCandidateFile(fileName!, includePatterns, null))
            .ToList();

        Assert.Contains("f.txt", found);
        Assert.DoesNotContain(found, f => f!.Contains(".bak.len.tmp"));
    }

    [Fact]
    public void IsCandidateFile_StillHonorsIncludeAndExclude()
    {
        List<Regex> includePatterns = FilePatternMatcher.Compile(["*.cs"]);
        List<Regex> excludePatterns = FilePatternMatcher.Compile(["*.designer.cs"]);

        Assert.True(DirectoryTraversal.IsCandidateFile("Foo.cs", includePatterns, excludePatterns));
        Assert.False(DirectoryTraversal.IsCandidateFile("Foo.txt", includePatterns, excludePatterns));
        Assert.False(DirectoryTraversal.IsCandidateFile("Foo.designer.cs", includePatterns, excludePatterns));
    }

    [Fact]
    public void DirectoryJunction_IsNotDescendedInto()
    {
        using var dir = new TempDirectory();

        dir.WriteFile("real/keep.txt", "a"u8.ToArray());

        string real = dir.CombinePath("real");
        string loopLink = Path.Combine(real, "loop");

        // A self-referencing junction: if the walker ever followed it, this
        // would recurse without bound. Junctions don't require admin rights.
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{loopLink}\" \"{real}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit(10000);

        if (proc.ExitCode != 0 || !Directory.Exists(loopLink))
        {
            // mklink unavailable/blocked in this environment; nothing to
            // verify, but don't fail the suite over an environment gap.
            return;
        }

        try
        {
            Assert.True(DirectoryTraversal.IsReparsePointDirectory(loopLink));
            Assert.True(
                DirectoryTraversal.ShouldSkipFile(
                    loopLink,
                    out string? reason));
            Assert.Contains(
                "reparse point",
                reason,
                StringComparison.OrdinalIgnoreCase);

            // Bounded: if the walker ignored the reparse-point check, this
            // would never return.
            var found = DirectoryTraversal.EnumerateCandidateFiles(dir.Path)
                .Select(Path.GetFileName)
                .ToList();

            Assert.Contains("keep.txt", found);
            Assert.DoesNotContain(found, f => f == "loop");
        }
        finally
        {
            System.Diagnostics.Process.Start("cmd.exe", $"/c rmdir \"{loopLink}\"")?.WaitForExit(5000);
        }
    }
}

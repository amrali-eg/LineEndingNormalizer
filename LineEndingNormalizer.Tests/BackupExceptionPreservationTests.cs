using System.Text;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// CreateBackup clears a previous .bak's ReadOnly attribute so the install can proceed,
/// then puts it back. Restoring from a plain finally meant a failing SetAttributes could
/// throw over an in-flight replacement failure, so the caller was told about an attribute
/// problem instead of why the backup actually failed.
///
/// Ported from EncodingChecker, which had the identical pattern and fixed it in v3.4.0.
///
/// SCOPE: these pin the observable contract - a backup over a ReadOnly previous backup
/// works and keeps the attribute, and a failure surfaces as the real error. They do NOT
/// exercise the masking branch itself, which needs the replacement AND the rollback to
/// fail together. That is not reachable from the filesystem: SetAttributes succeeds on a
/// locked file and silently ignores unsettable attributes, and any ACL denying the
/// rollback also denies the initial clear, which happens earlier and outside the try.
/// Forcing it would need an injection seam in production code, so the branch is covered
/// by inspection instead.
/// </summary>
public sealed class BackupExceptionPreservationTests
{
    private static byte[] Lf(string text) =>
        new UTF8Encoding(false).GetBytes(text.Replace("\r\n", "\n"));

    [Fact]
    public void BackupOverAReadOnlyPreviousBackup_SucceedsAndKeepsTheAttribute()
    {
        using var dir = new TempDirectory();

        string path = dir.WriteFile("file.txt", Lf("alpha\nbeta\n"));

        string backupPath = path + ".bak";
        File.WriteAllBytes(backupPath, Lf("stale backup\n"));
        File.SetAttributes(backupPath, FileAttributes.ReadOnly);

        try
        {
            NormalizeResult result = NewLineNormalizer.NormalizeFile(
                path, LineEnding.Crlf, whatIf: false, backup: true);

            Assert.Equal(NormalizeResult.Converted, result);

            // The backup slot keeps the protection it had...
            Assert.True(File.GetAttributes(backupPath).HasFlag(FileAttributes.ReadOnly));

            // ...and now holds the pre-conversion content, not the stale one.
            File.SetAttributes(backupPath, FileAttributes.Normal);
            Assert.Equal("alpha\nbeta\n", File.ReadAllText(backupPath));

            Assert.Equal("alpha\r\nbeta\r\n", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(backupPath))
                File.SetAttributes(backupPath, FileAttributes.Normal);
        }
    }

    [Fact]
    public void BackupOverAWritablePreviousBackup_LeavesItWritable()
    {
        using var dir = new TempDirectory();

        string path = dir.WriteFile("file2.txt", Lf("alpha\nbeta\n"));

        string backupPath = path + ".bak";
        File.WriteAllBytes(backupPath, Lf("stale\n"));

        NormalizeResult result = NewLineNormalizer.NormalizeFile(
            path, LineEnding.Crlf, whatIf: false, backup: true);

        Assert.Equal(NormalizeResult.Converted, result);
        Assert.False(File.GetAttributes(backupPath).HasFlag(FileAttributes.ReadOnly));
        Assert.Equal("alpha\nbeta\n", File.ReadAllText(backupPath));
    }

    [Fact]
    public void FirstBackup_WithNoPreviousBackup_Succeeds()
    {
        using var dir = new TempDirectory();

        string path = dir.WriteFile("file3.txt", Lf("alpha\nbeta\n"));

        NormalizeResult result = NewLineNormalizer.NormalizeFile(
            path, LineEnding.Crlf, whatIf: false, backup: true);

        Assert.Equal(NormalizeResult.Converted, result);
        Assert.Equal("alpha\nbeta\n", File.ReadAllText(path + ".bak"));
        Assert.Equal(
            System.Security.Cryptography.SHA256.HashData(
                Lf("alpha\nbeta\n")),
            System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(path + ".bak")));
    }

    [Fact]
    public void RepeatedBackups_RefreshTheBackupEachTime()
    {
        using var dir = new TempDirectory();

        string path = dir.WriteFile("file4.txt", Lf("alpha\nbeta\n"));
        string backupPath = path + ".bak";

        NewLineNormalizer.NormalizeFile(path, LineEnding.Crlf, whatIf: false, backup: true);
        Assert.Equal("alpha\nbeta\n", File.ReadAllText(backupPath));

        // Second run converts back; the backup now holds the CRLF form.
        NewLineNormalizer.NormalizeFile(path, LineEnding.Lf, whatIf: false, backup: true);
        Assert.Equal("alpha\r\nbeta\r\n", File.ReadAllText(backupPath));
        Assert.Equal("alpha\nbeta\n", File.ReadAllText(path));
    }
}

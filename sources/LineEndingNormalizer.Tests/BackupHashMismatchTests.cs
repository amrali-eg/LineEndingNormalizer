using System.Reflection;
using System.Text;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// LosslessFileWriter.CreateBackup re-reads the source from disk and hashes it a
/// second time, refusing to install the backup if that hash disagrees with the
/// SourceSha256 captured earlier during conversion; VerifyFileSha256 re-checks the
/// installed .bak the same way. Both throws exist specifically to catch the source
/// or the freshly-written backup changing out from under a conversion, and neither
/// had a test that actually forces the mismatch - the normal filesystem paths in
/// this suite only ever produce a match.
///
/// CreateBackup and VerifyFileSha256 are private with no public seam to force a
/// mismatch deterministically (the real trigger is a race between the initial
/// source read and this later re-read), so these call them directly via
/// reflection. This exercises the exact comparison and throw this project's own
/// code review flagged as untested, without relying on a timing-dependent race.
/// </summary>
public sealed class BackupHashMismatchTests
{
    private static byte[] Lf(string text) =>
        new UTF8Encoding(false).GetBytes(text.Replace("\r\n", "\n"));

    private static readonly byte[] WrongHash = new byte[32];

    private static void InvokeCreateBackup(
        string path, string directory, byte[] expectedSourceSha256)
    {
        MethodInfo method =
            typeof(NewLineNormalizer).Assembly
                .GetType("LineEndingNormalizer.LosslessFileWriter")!
                .GetMethod(
                    "CreateBackup",
                    BindingFlags.NonPublic | BindingFlags.Static)!;

        try
        {
            method.Invoke(
                null,
                [path, directory, expectedSourceSha256, CancellationToken.None]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void InvokeVerifyFileSha256(string path, byte[] expectedSha256)
    {
        MethodInfo method =
            typeof(NewLineNormalizer).Assembly
                .GetType("LineEndingNormalizer.LosslessFileWriter")!
                .GetMethod(
                    "VerifyFileSha256",
                    BindingFlags.NonPublic | BindingFlags.Static)!;

        try
        {
            method.Invoke(
                null,
                [path, expectedSha256, CancellationToken.None]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    [Fact]
    public void CreateBackup_WhenSourceHashDisagreesWithCapturedHash_RefusesAndLeavesSourceUntouched()
    {
        using var dir = new TempDirectory();

        byte[] originalBytes = Lf("alpha\nbeta\n");
        string path = dir.WriteFile("file.txt", originalBytes);

        ConversionRefusedException ex = Assert.Throws<ConversionRefusedException>(
            () => InvokeCreateBackup(path, dir.Path, WrongHash));

        Assert.Equal("BackupVerificationFailed", ex.ReasonCode);

        // The source itself is only ever read here, never written.
        Assert.Equal(originalBytes, File.ReadAllBytes(path));

        // Nothing was installed, and the finally block cleaned up its temp copy.
        Assert.False(File.Exists(path + ".bak"));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.len.tmp"));
    }

    [Fact]
    public void VerifyFileSha256_WhenInstalledBackupDisagreesWithCapturedHash_Refuses()
    {
        using var dir = new TempDirectory();

        string backupPath = dir.WriteFile("file.txt.bak", Lf("alpha\nbeta\n"));

        ConversionRefusedException ex = Assert.Throws<ConversionRefusedException>(
            () => InvokeVerifyFileSha256(backupPath, WrongHash));

        Assert.Equal("BackupVerificationFailed", ex.ReasonCode);

        // Verification only reads; the installed backup is left exactly as it was.
        Assert.Equal(Lf("alpha\nbeta\n"), File.ReadAllBytes(backupPath));
    }
}

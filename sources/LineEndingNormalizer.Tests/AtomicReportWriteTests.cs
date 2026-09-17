using System.Text;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// A failed report write must preserve the previous complete report.
/// </summary>
/// <remarks>
/// Ported from EncodingChecker's BL-24 fix. Before it, -Report was written by
/// truncating the destination in place: an interrupted write left the report
/// absent or half-written, and a script re-reading the last known-good
/// report after a failed run would find nothing there instead.
/// </remarks>
public sealed class AtomicReportWriteTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("len-atomic-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private string Existing(string name, string content)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private string[] TempArtifacts() =>
        Directory.GetFiles(_root, "*." + LosslessFileWriter.TempFileSuffix);

    [Fact]
    public void AFailedWriteLeavesThePreviousReportIntact()
    {
        string path = Existing("report.csv", "old,content\r\n");
        byte[] before = File.ReadAllBytes(path);

        string? error = Program.WriteArtifactAtomically(
            path, _ => throw new InvalidOperationException("writer gave up"));

        Assert.NotNull(error);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(TempArtifacts());
    }

    [Fact]
    public void ASuccessfulWriteReplacesTheReportAndLeavesNoTemporaryFile()
    {
        string path = Existing("report.csv", "old,content\r\n");

        string? error = Program.WriteArtifactAtomically(
            path,
            stream =>
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write("new,content\r\n");
            });

        Assert.Null(error);
        Assert.Equal("new,content\r\n", File.ReadAllText(path));
        Assert.Empty(TempArtifacts());
    }

    [Fact]
    public void ADestinationThatDoesNotExistYetIsCreated()
    {
        string path = Path.Combine(_root, "fresh.csv");

        string? error = Program.WriteArtifactAtomically(
            path,
            stream =>
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write("header\r\n");
            });

        Assert.Null(error);
        Assert.Equal("header\r\n", File.ReadAllText(path));
        Assert.Empty(TempArtifacts());
    }

    /// <summary>The temporary file must use the suffix DirectoryTraversal already excludes.</summary>
    [Fact]
    public void TheTemporaryFileUsesTheSuffixScansAlreadyExclude()
    {
        string path = Path.Combine(_root, "observed.csv");
        string? observed = null;

        Program.WriteArtifactAtomically(
            path,
            _ => observed = TempArtifacts().SingleOrDefault());

        Assert.NotNull(observed);
        Assert.EndsWith("." + LosslessFileWriter.TempFileSuffix, observed);
        Assert.Empty(TempArtifacts());
    }
}

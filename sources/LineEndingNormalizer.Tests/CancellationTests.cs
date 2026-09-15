using System.Text;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// Cancellation support added for Ctrl+C at the CLI entry point (Program.Main). Neither
/// this project nor the sibling EncodingChecker project can unit-test genuine OS-level
/// Console.CancelKeyPress delivery, so this is scoped to "a cancelled token is actually
/// observed and propagates," not literal Ctrl+C.
/// </summary>
public sealed class CancellationTests
{
    [Fact]
    public void NormalizeFile_PreCancelledToken_ThrowsBeforeWriting()
    {
        using var dir = new TempDirectory();

        byte[] original = Encoding.ASCII.GetBytes("a\nb\n");
        string path = dir.WriteFile("needs_convert.txt", original);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            NewLineNormalizer.NormalizeFile(
                path,
                LineEnding.Crlf,
                whatIf: false,
                backup: false,
                cts.Token));

        // Nothing was written: the file must be untouched by the cancelled attempt.
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void DetectFile_PreCancelledToken_ThrowsImmediately()
    {
        using var dir = new TempDirectory();

        string path = dir.WriteFile("a.txt", Encoding.ASCII.GetBytes("a\r\nb\r\n"));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            NewLineNormalizer.DetectFile(path, cts.Token));
    }

    [Fact]
    public void CreateParallelOptions_PropagatesTheGivenToken()
    {
        using var cts = new CancellationTokenSource();

        ParallelOptions options = Program.CreateParallelOptions(new Options(), cts.Token);

        Assert.Equal(cts.Token, options.CancellationToken);
    }
}

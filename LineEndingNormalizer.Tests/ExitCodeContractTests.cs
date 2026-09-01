using System.Text;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// LEN's exit codes are a published CLI contract, and codes 0-5 are deliberately
/// identical to EncodingChecker's so a script driving both tools can share one
/// mapping - including 5 for a safe refusal. Code 6 refines a case
/// EncodingChecker reports as 1, so no code means two different things across
/// the two tools.
///
/// These tests pin the numbers themselves, not just "non-zero" - renumbering is a
/// breaking change for CI gates and must not happen silently.
///
/// Redirects the process-global Console.Out/Error, so they rely on
/// AssemblyInfo.cs's [assembly: CollectionBehavior(DisableTestParallelization = true)]
/// like the other end-to-end tests.
/// </summary>
public sealed class ExitCodeContractTests
{
    private const int ExpectedSuccess = 0;
    private const int ExpectedInvalidArguments = 1;
    private const int ExpectedChangesNeeded = 2;
    private const int ExpectedProcessingErrors = 3;
    private const int ExpectedSafeRefusal = 5;
    private const int ExpectedReparsePointRoot = 6;

    private static int RunMain(params string[] args)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;

        try
        {
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());

            return Program.Main(args);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static byte[] Lf(string text) =>
        System.Text.Encoding.ASCII.GetBytes(text.Replace("\r\n", "\n"));

    [Fact]
    public void Help_ExitsZero()
    {
        Assert.Equal(ExpectedSuccess, RunMain("-?"));
    }

    [Fact]
    public void CleanRun_ExitsZero()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("a.txt", Lf("alpha\nbeta\n"));

        Assert.Equal(
            ExpectedSuccess,
            RunMain("-BasePath", dir.Path, "-Include", "*.txt", "-Target", "CRLF"));
    }

    [Fact]
    public void UnknownArgument_ExitsOne()
    {
        using var dir = new TempDirectory();

        Assert.Equal(
            ExpectedInvalidArguments,
            RunMain("-BasePath", dir.Path, "-NoSuchSwitch"));
    }

    [Fact]
    public void MissingArgumentValue_ExitsOne()
    {
        Assert.Equal(ExpectedInvalidArguments, RunMain("-BasePath"));
    }

    [Fact]
    public void FailOnChanges_WithFilesNeedingConversion_ExitsTwo()
    {
        // The CI-gate code, and the one most likely to be scripted: it must stay 2,
        // matching EncodingChecker's -FailOnChanges.
        using var dir = new TempDirectory();
        dir.WriteFile("a.txt", Lf("alpha\nbeta\n"));

        Assert.Equal(
            ExpectedChangesNeeded,
            RunMain(
                "-BasePath", dir.Path,
                "-Include", "*.txt",
                "-Target", "CRLF",
                "-WhatIf",
                "-FailOnChanges"));
    }

    [Fact]
    public void FailOnChanges_WithNothingToConvert_ExitsZero()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("a.txt", System.Text.Encoding.ASCII.GetBytes("alpha\r\nbeta\r\n"));

        Assert.Equal(
            ExpectedSuccess,
            RunMain(
                "-BasePath", dir.Path,
                "-Include", "*.txt",
                "-Target", "CRLF",
                "-FailOnChanges"));
    }

    [Fact]
    public void UnwritableReportPath_ExitsThree()
    {
        // A report that cannot be written is a processing failure, not a usage error:
        // by this point the files have already been converted.
        using var dir = new TempDirectory();
        dir.WriteFile("a.txt", Lf("alpha\nbeta\n"));

        // A directory occupying the report path makes the write fail deterministically.
        string reportPath = dir.CombinePath("report.csv");
        Directory.CreateDirectory(reportPath);

        Assert.Equal(
            ExpectedProcessingErrors,
            RunMain(
                "-BasePath", dir.Path,
                "-Include", "*.txt",
                "-Target", "CRLF",
                "-Report", reportPath));
    }

    [Fact]
    public void MissingBaseDirectory_ExitsInvalidArguments()
    {
        // A directory that does not exist is a bad invocation, not a refusal.
        // Code 5 is reserved for the same meaning EncodingChecker gives it.
        using var dir = new TempDirectory();
        string missing = dir.CombinePath("no-such-directory");

        Assert.Equal(
            ExpectedInvalidArguments,
            RunMain("-BasePath", missing, "-Include", "*.txt", "-Target", "CRLF"));
    }

    [Fact]
    public void ReparsePointBasePath_ExitsSix()
    {
        using var dir = new TempDirectory();

        string target = dir.CombinePath("real");
        Directory.CreateDirectory(target);

        string junction = dir.CombinePath("link");

        if (!TryCreateJunction(junction, target))
        {
            // Creating a junction can require privileges the test host lacks; skipping
            // is better than asserting something weaker and calling it coverage.
            return;
        }

        Assert.Equal(
            ExpectedReparsePointRoot,
            RunMain("-BasePath", junction, "-Include", "*.txt", "-Target", "CRLF"));
    }

    [Fact]
    public void SafeRefusal_ExitsFive_AndLeavesTheSourceUnchanged()
    {
        using var dir = new TempDirectory();

        // BOM-less UTF-16LE holding plain ASCII: byte-swapped it is valid CJK,
        // so neither byte order can be ruled out and LEN must decline.
        byte[] original = Encoding.Unicode.GetBytes("hello\nworld\n");
        string path = dir.WriteFile("ambiguous.txt", original);

        Assert.Equal(
            ExpectedSafeRefusal,
            RunMain("-BasePath", dir.Path, "-Target", "CRLF"));

        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void ProcessingError_OutranksSafeRefusal()
    {
        // A run holding both must report the failure: a refusal is a correct
        // outcome, so it must never mask one that is not.
        using var dir = new TempDirectory();
        using var reportDir = new TempDirectory();

        dir.WriteFile("ambiguous.txt", Encoding.Unicode.GetBytes("hello\nworld\n"));

        // An unwritable report path is the reachable processing failure here.
        string unwritable =
            Path.Combine(reportDir.CombinePath("no-such-directory"), "report.csv");

        Assert.Equal(
            ExpectedProcessingErrors,
            RunMain(
                "-BasePath", dir.Path,
                "-Target", "CRLF",
                "-Report", unwritable));
    }

    [Fact]
    public void SafeRefusal_OutranksFailOnChanges()
    {
        // -FailOnChanges cannot speak for a file whose conversion was refused,
        // so the refusal is the more informative answer.
        using var dir = new TempDirectory();

        dir.WriteFile("ambiguous.txt", Encoding.Unicode.GetBytes("hello\nworld\n"));
        dir.WriteFile("converts.txt", "a\nb\n"u8.ToArray());

        Assert.Equal(
            ExpectedSafeRefusal,
            RunMain(
                "-BasePath", dir.Path,
                "-Target", "CRLF",
                "-WhatIf",
                "-FailOnChanges"));
    }

    [Fact]
    public void FailOnChanges_StillWinsWhenNothingWasRefused()
    {
        // The precedence must not swallow the case it was built around.
        using var dir = new TempDirectory();

        dir.WriteFile("converts.txt", "a\nb\n"u8.ToArray());

        Assert.Equal(
            ExpectedChangesNeeded,
            RunMain(
                "-BasePath", dir.Path,
                "-Target", "CRLF",
                "-WhatIf",
                "-FailOnChanges"));
    }

    [Fact]
    public void Version_IsReportedWithoutABasePath()
    {
        // The release check reads the version from the binary itself, so this
        // must not require a scan to be set up first.
        Assert.Equal(ExpectedSuccess, RunMain("--version"));
    }

    [Fact]
    public void EveryExitCodeIsDistinct()
    {
        int[] codes =
        [
            ExpectedSuccess,
            ExpectedInvalidArguments,
            ExpectedChangesNeeded,
            ExpectedProcessingErrors,
            4, // cancelled; exercised by CancellationTests rather than through Main
            ExpectedSafeRefusal,
            ExpectedReparsePointRoot,
        ];

        Assert.Equal(codes.Length, codes.Distinct().Count());
    }

    private static bool TryCreateJunction(string junction, string target)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /J \"{junction}\" \"{target}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit();

            return process.ExitCode == 0 && Directory.Exists(junction);
        }
        catch (Exception ex) when (
            ex is System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }
}

using System.Text;

namespace LineEndingNormalizer.Tests;

/// <summary>
/// End-to-end coverage for Program.Main: -Deterministic ordering, -Report
/// CSV content, and -ValidateOnly behavior/exit codes. Redirects the
/// process-global Console.Out/Error around each call, so these tests must
/// run sequentially with the rest of the suite -- see
/// AssemblyInfo.cs's [assembly: CollectionBehavior(DisableTestParallelization = true)].
/// </summary>
public sealed class ProgramEndToEndTests
{
    private static int RunMain(string[] args, out string stdout, out string stderr)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;

        var outWriter = new StringWriter();
        var errorWriter = new StringWriter();

        try
        {
            Console.SetOut(outWriter);
            Console.SetError(errorWriter);

            return Program.Main(args);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);

            stdout = outWriter.ToString();
            stderr = errorWriter.ToString();
        }
    }

    [Fact]
    public void Deterministic_EmitsFilesInOrdinalLexicalOrder()
    {
        using var dir = new TempDirectory();

        // Names chosen so ordinal order differs from creation order.
        string[] names = ["mango.txt", "apple.txt", "zebra.txt", "banana.txt", "kiwi.txt", "fig.txt"];

        foreach (string name in names)
        {
            dir.WriteFile(name, Encoding.ASCII.GetBytes("line one\nline two\n"));
        }

        int exitCode = RunMain(
            ["-BasePath", dir.Path, "-Target", "CRLF", "-WhatIf", "-Deterministic"],
            out string stdout,
            out _);

        Assert.Equal(0, exitCode);

        List<string> emittedOrder =
            [.. names.OrderBy(n => n, StringComparer.Ordinal)];

        int lastIndex = -1;

        foreach (string name in emittedOrder)
        {
            int index = stdout.IndexOf("Would convert: " + name, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Expected to find 'Would convert: {name}' in output.");
            Assert.True(index > lastIndex, $"'{name}' appeared out of ordinal order.");
            lastIndex = index;
        }
    }

    [Fact]
    public void Report_WritesCsvWithExpectedSchema()
    {
        using var dir = new TempDirectory();

        dir.WriteFile("already.txt", Encoding.ASCII.GetBytes("a\r\nb\r\n"));
        dir.WriteFile("needsconvert.txt", Encoding.ASCII.GetBytes("a\nb\n"));

        string reportPath = dir.CombinePath("report.csv");

        int exitCode = RunMain(
            ["-BasePath", dir.Path, "-Target", "CRLF", "-WhatIf", "-Report", reportPath, "-Quiet"],
            out _,
            out _);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(reportPath));

        string[] lines = File.ReadAllLines(reportPath);

        Assert.Equal(
            "File,Encoding,BOM,LineEnding,Target,Result,ReasonCode,Diagnostic",
            lines[0]);

        string? alreadyRow = lines.FirstOrDefault(l => l.StartsWith("already.txt,", StringComparison.Ordinal));
        string? needsConvertRow = lines.FirstOrDefault(l => l.StartsWith("needsconvert.txt,", StringComparison.Ordinal));

        Assert.NotNull(alreadyRow);
        Assert.NotNull(needsConvertRow);

        Assert.Equal("already.txt,ASCII,No,CRLF,CRLF,Unchanged,,", alreadyRow);
        Assert.Equal("needsconvert.txt,ASCII,No,LF,CRLF,Would convert,,", needsConvertRow);
    }

    [Fact]
    public void Report_BackupFailureIncludesStableReasonAndDiagnostic()
    {
        using var dir = new TempDirectory();
        using var reportDir = new TempDirectory();

        string sourcePath =
            dir.WriteFile(
                "backupfail.txt",
                Encoding.ASCII.GetBytes("a\nb\n"));

        Directory.CreateDirectory(sourcePath + ".bak");

        string reportPath =
            reportDir.CombinePath("report.csv");

        int exitCode = RunMain(
            [
                "-BasePath", dir.Path,
                "-Target", "CRLF",
                "-Backup",
                "-Report", reportPath,
                "-Quiet"
            ],
            out _,
            out _);

        Assert.Equal(3, exitCode);
        Assert.Equal(
            Encoding.ASCII.GetBytes("a\nb\n"),
            File.ReadAllBytes(sourcePath));

        string row =
            Assert.Single(
                File.ReadAllLines(reportPath).Skip(1));

        Assert.Contains(",Error,AccessDenied,", row, StringComparison.Ordinal);
        Assert.Contains("backup", row, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Report_AmbiguousBomlessUtf16IncludesRefusalReason()
    {
        using var dir = new TempDirectory();
        using var reportDir = new TempDirectory();

        var text = new StringBuilder();

        for (int i = 0; i < 20; i++)
        {
            text.Append('\u4100');
            text.Append('\u0A00');
            text.Append('\u4200');
        }

        string sourcePath =
            dir.WriteFile(
                "ambiguous.txt",
                new UnicodeEncoding(
                        bigEndian: true,
                        byteOrderMark: false,
                        throwOnInvalidBytes: true)
                    .GetBytes(text.ToString()));

        byte[] original = File.ReadAllBytes(sourcePath);
        string reportPath = reportDir.CombinePath("report.csv");

        int exitCode = RunMain(
            [
                "-BasePath", dir.Path,
                "-Target", "CRLF",
                "-WhatIf",
                "-Report", reportPath,
                "-Quiet"
            ],
            out _,
            out _);

        // 5, not 3: LEN worked correctly and declined. Reporting a refusal as a
        // processing failure would make a safe outcome indistinguishable from a
        // broken one for anything reading the exit code.
        Assert.Equal(5, exitCode);
        Assert.Equal(original, File.ReadAllBytes(sourcePath));

        string row =
            Assert.Single(
                File.ReadAllLines(reportPath).Skip(1));

        Assert.Contains(
            ",Refused,AmbiguousBomlessUtf16,",
            row,
            StringComparison.Ordinal);
        Assert.Contains(
            "valid as both",
            row,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Report_RowCountUnaffectedByQuietOrVerbose()
    {
        using var dir = new TempDirectory();
        using var reportDir = new TempDirectory();

        dir.WriteFile("a.txt", Encoding.ASCII.GetBytes("x\r\n"));
        dir.WriteFile("b.txt", Encoding.ASCII.GetBytes("x\n"));

        // Written outside the scanned directory: a report file dropped
        // inside it would otherwise be picked up as an extra candidate by
        // the second run, since -Include defaults to "*".
        string quietReport = reportDir.CombinePath("quiet.csv");
        string verboseReport = reportDir.CombinePath("verbose.csv");

        RunMain(["-BasePath", dir.Path, "-Target", "CRLF", "-WhatIf", "-Quiet", "-Report", quietReport], out _, out _);
        RunMain(["-BasePath", dir.Path, "-Target", "CRLF", "-WhatIf", "-Verbose", "-Report", verboseReport], out _, out _);

        // Header + 2 data rows either way.
        Assert.Equal(3, File.ReadAllLines(quietReport).Length);
        Assert.Equal(3, File.ReadAllLines(verboseReport).Length);
    }

    [Fact]
    public void Report_InsideScannedDirectory_IsNeverRescannedOnALaterRun()
    {
        using var dir = new TempDirectory();

        dir.WriteFile("a.txt", Encoding.ASCII.GetBytes("x\r\n"));
        dir.WriteFile("b.txt", Encoding.ASCII.GetBytes("x\n"));

        // Unlike Report_RowCountUnaffectedByQuietOrVerbose, this report path is
        // deliberately inside the scanned directory - excludedFullPath in
        // DirectoryTraversal.EnumerateCandidateFiles must keep it out of both
        // this run's own candidate list and any later run's.
        string reportPath = dir.CombinePath("report.csv");

        RunMain(["-BasePath", dir.Path, "-Target", "CRLF", "-WhatIf", "-Quiet", "-Report", reportPath], out _, out _);

        // Header + 2 data rows; the report itself must not appear as a third row.
        Assert.Equal(3, File.ReadAllLines(reportPath).Length);

        RunMain(["-BasePath", dir.Path, "-Target", "CRLF", "-WhatIf", "-Quiet", "-Report", reportPath], out _, out _);

        // Second run: still just the 2 real files, not 3 (the report from run 1).
        Assert.Equal(3, File.ReadAllLines(reportPath).Length);
    }

    [Fact]
    public void Report_RelativeBasePath_IsNeverRescannedOnALaterRun()
    {
        using var dir = new TempDirectory();

        dir.WriteFile("a.txt", Encoding.ASCII.GetBytes("x\r\n"));
        dir.WriteFile("b.txt", Encoding.ASCII.GetBytes("x\n"));

        string originalDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = dir.Path;

            // A relative -BasePath is the case that broke self-exclusion:
            // enumerated candidate paths stayed relative while the
            // -Report exclusion check compared against an absolute path.
            RunMain(["-BasePath", ".", "-Target", "CRLF", "-WhatIf", "-Quiet", "-Report", "report.csv"], out _, out _);

            string[] firstRunLines = File.ReadAllLines(Path.Combine(dir.Path, "report.csv"));

            // Header + 2 data rows; the report itself must not appear as a third row.
            Assert.Equal(3, firstRunLines.Length);

            RunMain(["-BasePath", ".", "-Target", "CRLF", "-WhatIf", "-Quiet", "-Report", "report.csv"], out _, out _);

            string[] secondRunLines = File.ReadAllLines(Path.Combine(dir.Path, "report.csv"));

            // Second run: still just the 2 real files, not 3 (the report from run 1).
            Assert.Equal(3, secondRunLines.Length);
            Assert.DoesNotContain(secondRunLines, line => line.StartsWith("report.csv,", StringComparison.Ordinal));
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
        }
    }

    [Fact]
    public void ValidateOnly_HidesAlreadyNormalizedByDefault_ShowsUnderVerbose()
    {
        using var dir = new TempDirectory();

        dir.WriteFile("normalized.txt", Encoding.ASCII.GetBytes("a\r\nb\r\n"));
        dir.WriteFile("invalid.txt", Encoding.ASCII.GetBytes("a\nb\n"));

        RunMain(
            ["-BasePath", dir.Path, "-Target", "CRLF", "-ValidateOnly"],
            out string defaultStdout,
            out _);

        Assert.Contains("Invalid: invalid.txt", defaultStdout);
        Assert.DoesNotContain("normalized.txt", defaultStdout);

        RunMain(
            ["-BasePath", dir.Path, "-Target", "CRLF", "-ValidateOnly", "-Verbose"],
            out string verboseStdout,
            out _);

        Assert.Contains("Invalid: invalid.txt", verboseStdout);
        Assert.Contains("OK     normalized.txt", verboseStdout);
    }

    [Fact]
    public void ValidateOnly_NeverModifiesFiles()
    {
        using var dir = new TempDirectory();

        byte[] originalBytes = Encoding.ASCII.GetBytes("a\nb\n");
        string path = dir.WriteFile("invalid.txt", originalBytes);

        RunMain(["-BasePath", dir.Path, "-Target", "CRLF", "-ValidateOnly"], out _, out _);

        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void LinkedFiles_AreSkippedInEveryMode()
    {
        using var root = new TempDirectory();
        using var targetDirectory = new TempDirectory();

        string target =
            targetDirectory.WriteFile(
                "target.txt",
                Encoding.ASCII.GetBytes("a\nb\n"));

        string linked = root.CombinePath("linked.txt");

        try
        {
            File.CreateSymbolicLink(linked, target);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            PlatformNotSupportedException)
        {
            // Some Windows hosts do not permit creating symlinks. The
            // attribute-level rule remains covered by BackupAndReparseWalkTests.
            return;
        }

        string[][] modeArguments =
        [
            ["-Target", "CRLF"],
            ["-Target", "CRLF", "-WhatIf"],
            ["-Target", "CRLF", "-ValidateOnly"],
            ["-DetectOnly"]
        ];

        byte[] original = File.ReadAllBytes(target);

        foreach (string[] mode in modeArguments)
        {
            string[] arguments =
            ["-BasePath", root.Path, "-Include", "*", .. mode];

            int exitCode =
                RunMain(
                    arguments,
                    out string stdout,
                    out string stderr);

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain("linked.txt", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("linked.txt", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(original, File.ReadAllBytes(target));
        }
    }

    [Fact]
    public void ValidateOnly_FailOnChanges_ExitsChangesNeeded_WhenInvalidFilesExist()
    {
        using var dir = new TempDirectory();

        dir.WriteFile("invalid.txt", Encoding.ASCII.GetBytes("a\nb\n"));

        int exitCode = RunMain(
            ["-BasePath", dir.Path, "-Target", "CRLF", "-ValidateOnly", "-FailOnChanges", "-Quiet"],
            out _,
            out _);

        // 2, matching EncodingChecker's -FailOnChanges (see ExitCodeContractTests).
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void ValidateOnly_FailOnChanges_ExitsSuccess_WhenAllNormalized()
    {
        using var dir = new TempDirectory();

        dir.WriteFile("normalized.txt", Encoding.ASCII.GetBytes("a\r\nb\r\n"));

        int exitCode = RunMain(
            ["-BasePath", dir.Path, "-Target", "CRLF", "-ValidateOnly", "-FailOnChanges", "-Quiet"],
            out _,
            out _);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ReparsePointBasePath_ExitsWithDedicatedCode_NothingScanned()
    {
        using var dir = new TempDirectory();

        string real = dir.CombinePath("real");
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "file.txt"), "hello");

        string junction = dir.CombinePath("link");

        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{real}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit(10000);

        if (proc.ExitCode != 0 || !Directory.Exists(junction))
        {
            // mklink unavailable/blocked in this environment; nothing to
            // verify, but don't fail the suite over an environment gap.
            return;
        }

        try
        {
            int exitCode = RunMain(
                ["-BasePath", junction, "-Target", "CRLF"],
                out _,
                out string stderr);

            Assert.Equal(6, exitCode);
            Assert.Contains("reparse point", stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            System.Diagnostics.Process.Start("cmd.exe", $"/c rmdir \"{junction}\"")?.WaitForExit(5000);
        }
    }

    [Theory]
    [InlineData("-?")]
    [InlineData("-h")]
    [InlineData("/?")]
    [InlineData("/h")]
    [InlineData("--help")]
    public void HelpFlag_PrintsUsage_ExitsSuccess_NoDirectoryAccess(string flag)
    {
        // No -BasePath given at all: if this reached argument validation instead of
        // the dedicated help path, it would fail with "Base path is required."
        int exitCode = RunMain([flag], out string stdout, out _);

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "LineEndingNormalizer v" + Program.GetDisplayVersion(),
            stdout);
        Assert.Contains("Usage:", stdout);
    }
}

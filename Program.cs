using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace LineEndingNormalizer;

/// <summary>
/// CLI for detecting and normalizing line endings while preserving encoding,
/// BOM state, file content, and file metadata.
/// </summary>
internal static class Program
{
    // Codes 0-4 match EncodingChecker's, so a script driving both tools can share one
    // exit-code mapping. 5 and 6 refine cases EncodingChecker reports as 1; no code
    // means two different things across the two tools.

    /// <summary>
    /// Successful completion.
    /// </summary>
    private const int ExitSuccess = 0;

    /// <summary>
    /// Invalid command-line arguments.
    /// </summary>
    private const int ExitInvalidArguments = 1;

    /// <summary>
    /// -FailOnChanges detected files requiring conversion.
    /// </summary>
    private const int ExitChangesNeeded = 2;

    /// <summary>
    /// Processing or reporting errors occurred.
    /// </summary>
    private const int ExitProcessingErrors = 3;

    /// <summary>
    /// Scan cancelled by Ctrl+C.
    /// </summary>
    private const int ExitCancelled = 4;

    /// <summary>
    /// One or more files were safely refused: LEN declined to convert them
    /// because doing so could not be shown to be safe, and left them unchanged.
    /// </summary>
    /// <remarks>
    /// Matches EncodingChecker, so a script driving both tools can tell a
    /// deliberate refusal from a processing failure. A refusal is not an error:
    /// nothing went wrong and nothing was written.
    /// </remarks>
    private const int ExitSafeRefusal = 5;

    /// <summary>
    /// -BasePath is a reparse point. EncodingChecker reports this as
    /// <see cref="ExitInvalidArguments"/>.
    /// </summary>
    private const int ExitReparsePointRoot = 6;

    /// <summary>
    /// Serializes console output from parallel processing.
    /// </summary>
    private static readonly Lock ConsoleLock = new();

    private const int DefaultMaxParallelism = 4;

    /// <summary>
    /// Application entry point.
    /// </summary>
    public static int Main(string[] args)
    {
        if (args.Length == 1 &&
            args[0] is "/?" or "-?" or "/h" or "-h" or "--help")
        {
            PrintUsage();
            return ExitSuccess;
        }

        // Answered before argument validation and without -BasePath, so a
        // release check can read the version straight from the built binary.
        if (args.Length == 1 &&
            args[0] is "--version" or "-version" or "/version" or "-v")
        {
            Console.WriteLine(GetDisplayVersion());
            return ExitSuccess;
        }

        Encoding.RegisterProvider(
            CodePagesEncodingProvider.Instance);

        Options options;

        try
        {
            options = ParseArguments(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.WriteLine();

            PrintUsage();

            return ExitInvalidArguments;
        }

        if (!Directory.Exists(options.BasePath))
        {
            Console.Error.WriteLine(
                "Directory not found: {0}",
                options.BasePath);

            return ExitInvalidArguments;
        }

        // Candidate paths from directory traversal are always rooted at
        // BasePath as given; normalizing here keeps their comparison against
        // -Report's absolute path (self-exclusion) correct even when
        // -BasePath was passed as a relative path such as ".".
        options.BasePath = Path.GetFullPath(options.BasePath);

        if (DirectoryTraversal.IsReparsePointDirectory(options.BasePath))
        {
            Console.Error.WriteLine(
                "'{0}' is a symbolic link or other reparse point; " +
                "-BasePath must be a real directory.",
                options.BasePath);

            return ExitReparsePointRoot;
        }

        using var cancellation = new CancellationTokenSource();

        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            // Allow in-flight operations to stop safely.
            e.Cancel = true;
            cancellation.Cancel();
        };

        Console.CancelKeyPress += cancelHandler;

        try
        {
            if (options.DetectOnly)
            {
                // Keep detection strictly read-only.
                return RunDetectOnly(options, cancellation.Token);
            }

            ProcessingMode mode =
                options.ValidateOnly
                    ? ProcessingMode.ValidateOnly
                    : options.WhatIf
                        ? ProcessingMode.WhatIf
                        : ProcessingMode.Normalize;

            var statistics = new Statistics();

            Console.WriteLine(
                "LineEndingNormalizer v{0}",
                GetDisplayVersion());
            Console.WriteLine("Base path : {0}", options.BasePath);
            Console.WriteLine(
                "Patterns  : {0}",
                string.Join(", ", options.IncludePatterns.ToArray()));

            if (options.ExcludePatterns.Count > 0)
            {
                Console.WriteLine(
                    "Exclude   : {0}",
                    string.Join(", ", options.ExcludePatterns.ToArray()));
            }

            Console.WriteLine(
                "Target    : {0}",
                options.TargetLineEnding.ToString().ToUpperInvariant());

            if (mode == ProcessingMode.WhatIf)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(
                    "Mode      : Preview only (no files will be modified)");
                Console.ResetColor();
            }
            else if (mode == ProcessingMode.ValidateOnly)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(
                    "Mode      : Validate only (no files will be modified)");
                Console.ResetColor();
            }

            Console.WriteLine();

            bool reportOk =
                RunConvertOrValidate(
                    options,
                    statistics,
                    mode,
                    cancellation.Token);

            Console.WriteLine();

            PrintSummary(statistics, mode);

            // Precedence: a real failure outranks a refusal, and a refusal
            // outranks -FailOnChanges, because a refused file is one whose
            // conversion status -FailOnChanges cannot speak for.
            if (!reportOk ||
                statistics.Errors > 0)
            {
                return ExitProcessingErrors;
            }

            if (statistics.Refused > 0)
            {
                return ExitSafeRefusal;
            }

            if (options.FailOnChanges &&
                statistics.Converted > 0)
            {
                return ExitChangesNeeded;
            }

            return ExitSuccess;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Scan cancelled.");
            return ExitCancelled;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    /// <summary>
    /// Parses command-line arguments.
    ///
    /// Supported:
    ///   -BasePath <path>
    ///   -Include <patterns>
    ///   -Exclude <patterns>
    ///   -Target <CR|LF|CRLF>
    ///   -Verbose
    ///   -Quiet
    ///   -WhatIf
    ///   -ValidateOnly
    ///   -FailOnChanges
    ///   -Backup
    ///   -DetectOnly
    ///   -Deterministic
    ///   -Report <path>
    ///   -MaxParallelism <N>
    ///   -FullPath
    /// </summary>
    internal static Options ParseArguments(
        string[] args)
    {
        var options = new Options();
        bool includeSpecified = false;
        bool excludeSpecified = false;
        bool targetSpecified = false;

        for (int i = 0;
             i < args.Length;
             i++)
        {
            string arg = args[i];

            switch (arg.ToLowerInvariant())
            {
                case "-basepath":

                    if (!TryTakeValue(args, ref i, out string? basePath))
                    {
                        throw new ArgumentException(
                            "Missing value for -BasePath.");
                    }

                    options.BasePath = basePath;

                    break;

                case "-include":

                    includeSpecified = true;

                    if (!TryTakeValue(args, ref i, out string? include))
                    {
                        throw new ArgumentException(
                            "Missing value for -Include.");
                    }

                    foreach (var pattern in SplitPatterns(include))
                    {
                        options.IncludePatterns.Add(pattern);
                    }

                    break;

                case "-exclude":

                    excludeSpecified = true;

                    if (!TryTakeValue(args, ref i, out string? exclude))
                    {
                        throw new ArgumentException(
                            "Missing value for -Exclude.");
                    }

                    foreach (var pattern in SplitPatterns(exclude))
                    {
                        options.ExcludePatterns.Add(pattern);
                    }

                    break;

                case "-target":

                    targetSpecified = true;

                    if (!TryTakeValue(args, ref i, out string? target))
                    {
                        throw new ArgumentException(
                            "Missing value for -Target.");
                    }

                    options.TargetLineEnding =
                        target.ToUpperInvariant() switch
                        {
                            "CRLF" or "WINDOWS" => LineEnding.Crlf,
                            "LF" or "UNIX" => LineEnding.Lf,
                            "CR" or "MAC" => LineEnding.Cr,

                            _ => throw new ArgumentException(
                                "Invalid target line ending: " +
                                target)
                        };

                    break;

                case "-verbose":
                    options.Verbose = true;
                    break;

                case "-previewonly":

                    throw new ArgumentException(
                        "-PreviewOnly has been renamed to -WhatIf.");

                case "-whatif":
                    options.WhatIf = true;
                    break;

                case "-validateonly":
                    options.ValidateOnly = true;
                    break;

                case "-failonchanges":
                    options.FailOnChanges = true;
                    break;

                case "-quiet":
                    options.Quiet = true;
                    break;

                case "-backup":
                    options.Backup = true;
                    break;

                case "-detectonly":
                    options.DetectOnly = true;
                    break;

                case "-deterministic":
                    options.Deterministic = true;
                    break;

                case "-report":

                    if (!TryTakeValue(args, ref i, out string? report))
                    {
                        throw new ArgumentException(
                            "Missing value for -Report.");
                    }

                    options.Report = report;

                    break;

                case "-fullpath":
                    options.FullPath = true;
                    break;

                case "-maxparallelism":

                    if (!TryTakeValue(
                            args,
                            ref i,
                            out string? maxParallelismText,
                            allowLeadingDash: true))
                    {
                        throw new ArgumentException(
                            "Missing value for -MaxParallelism.");
                    }

                    if (!int.TryParse(
                            maxParallelismText,
                            out int maxParallelism) ||
                        maxParallelism < 1)
                    {
                        throw new ArgumentException(
                            "Invalid value for -MaxParallelism " +
                            "(must be a positive integer): " +
                            maxParallelismText);
                    }

                    options.MaxParallelism =
                        maxParallelism;

                    break;

                default:

                    throw new ArgumentException(
                        "Unknown argument: " +
                        arg);
            }
        }

        if (string.IsNullOrEmpty(options.BasePath))
        {
            throw new ArgumentException(
                "Base path is required.");
        }

        if (options.IncludePatterns.Count == 0)
        {
            if (includeSpecified)
            {
                throw new ArgumentException(
                    "-Include must contain at least one non-empty pattern.");
            }

            options.IncludePatterns.Add("*");
        }

        if (excludeSpecified &&
            options.ExcludePatterns.Count == 0)
        {
            throw new ArgumentException(
                "-Exclude must contain at least one non-empty pattern.");
        }

        ValidateOptionCompatibility(
            options,
            targetSpecified);

        return options;
    }

    private static void ValidateOptionCompatibility(
        Options options,
        bool targetSpecified)
    {
        if (options is { Verbose: true, Quiet: true })
        {
            throw new ArgumentException(
                "-Verbose and -Quiet cannot be combined.");
        }

        if (options is { DetectOnly: true, ValidateOnly: true })
        {
            throw new ArgumentException(
                "-DetectOnly and -ValidateOnly cannot be combined.");
        }

        if (options.DetectOnly &&
            (targetSpecified ||
             options.WhatIf ||
             options.Backup ||
             options.FailOnChanges ||
             options.Quiet ||
             options.Verbose))
        {
            throw new ArgumentException(
                "-DetectOnly cannot be combined with -Target, -WhatIf, -Backup, " +
                "-FailOnChanges, -Quiet, or -Verbose.");
        }

        if (options.ValidateOnly && options.WhatIf)
        {
            throw new ArgumentException(
                "-ValidateOnly and -WhatIf are separate read-only modes and cannot be combined.");
        }

        if (options.ValidateOnly && options.Backup)
        {
            throw new ArgumentException(
                "-Backup cannot be used with -ValidateOnly because validation never writes files.");
        }

        if (options.WhatIf && options.Backup)
        {
            throw new ArgumentException(
                "-Backup cannot be used with -WhatIf because preview mode never writes files.");
        }
    }

    private static bool TryTakeValue(
        string[] args,
        ref int i,
        [NotNullWhen(true)] out string? value,
        bool allowLeadingDash = false)
    {
        if (i + 1 >= args.Length)
        {
            value = null;
            return false;
        }

        string candidate = args[i + 1];

        if (!allowLeadingDash &&
            candidate.StartsWith('-'))
        {
            value = null;
            return false;
        }

        value = args[++i];
        return true;
    }

    /// <summary>
    /// Splits pattern lists and removes empty entries.
    /// </summary>
    private static string[] SplitPatterns(
        string arg)
    {
        return arg.Split(
            [
                ',',
                ';'
            ],
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Keeps traversal warnings consistent with other non-fatal diagnostics.
    /// </summary>
    private static void PrintTraversalWarning(string message)
    {
        lock (ConsoleLock)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Error.WriteLine(message);
            Console.ResetColor();
        }
    }

    #region Normalize / WhatIf / ValidateOnly

    /// <summary>
    /// Captures the processing result used by console and CSV output.
    /// </summary>
    private sealed record FileOutcome(
        string DisplayPath,
        string SortKey,
        NormalizeResult? Result,
        string ResultLabel,
        bool ConsoleVisible,
        DetectResult? Detected,
        bool IsError,
        string? ReasonCode,
        string? ErrorMessage);

    /// <summary>
    /// NormalizeFile's own scan runs before any write, so the detection data
    /// it returns always describes the original file, even when converted.
    /// </summary>
    private static FileOutcome ComputeFileOutcome(
        string file,
        Options options,
        ProcessingMode mode,
        Statistics statistics,
        CancellationToken cancellationToken)
    {
        statistics.IncrementChecked();

        string displayPath =
            FormatDisplayPath(
                file,
                options);

        bool previewOnly =
            mode != ProcessingMode.Normalize;

        bool backup =
            mode == ProcessingMode.Normalize &&
            options.Backup;

        DetectResult? detected = null;

        try
        {
            NormalizeResult result =
                NewLineNormalizer.NormalizeFile(
                    file,
                    options.TargetLineEnding,
                    previewOnly,
                    backup,
                    out detected,
                    cancellationToken);

            switch (result)
            {
                case NormalizeResult.Converted:
                    statistics.IncrementConverted();
                    break;

                case NormalizeResult.Unchanged:
                    statistics.IncrementUnchanged();
                    break;

                case NormalizeResult.EncodingNotDetected:
                    statistics.IncrementSkipped();
                    break;
            }

            return new FileOutcome(
                displayPath,
                file,
                result,
                GetResultLabel(result, mode),
                GetConsoleVisibility(result, options),
                detected,
                IsError: false,
                ReasonCode: null,
                ErrorMessage: null);
        }
        catch (ConversionRefusedException refused)
        {
            // LEN worked correctly and declined; nothing was written. Reporting
            // this as an error would make a safe outcome indistinguishable from
            // a failure for anything reading the result or the exit code.
            statistics.IncrementRefused();

            return new FileOutcome(
                displayPath,
                file,
                Result: null,
                ResultLabel: "Refused",
                ConsoleVisible: true,
                Detected: detected,
                IsError: false,
                ReasonCode: refused.ReasonCode,
                ErrorMessage: refused.Message);
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException)
        {
            statistics.IncrementErrors();

            return new FileOutcome(
                displayPath,
                file,
                Result: null,
                ResultLabel: "Error",
                ConsoleVisible: true,
                Detected: detected,
                IsError: true,
                ReasonCode: GetErrorReasonCode(ex),
                ErrorMessage: ex.Message);
        }
    }

    /// <summary>
    /// Gives reports a stable code while keeping the full exception message
    /// available for diagnosis.
    /// </summary>
    private static string GetErrorReasonCode(Exception exception)
    {
        // ConversionRefusedException is handled by its own catch before this
        // runs, so it deliberately has no arm here.
        return exception switch
        {
            DecoderFallbackException =>
                "InvalidEncoding",

            UnauthorizedAccessException =>
                "AccessDenied",

            IOException =>
                "IoError",

            _ =>
                "UnexpectedError"
        };
    }

    /// <summary>
    /// Maps an operation result to its mode-specific label.
    /// </summary>
    private static string GetResultLabel(
        NormalizeResult result,
        ProcessingMode mode)
    {
        return (result, mode) switch
        {
            (NormalizeResult.Converted,
                ProcessingMode.Normalize)
                => "Converted",

            (NormalizeResult.Converted,
                ProcessingMode.WhatIf)
                => "Would convert",

            (NormalizeResult.Converted,
                ProcessingMode.ValidateOnly)
                => "Invalid",

            (NormalizeResult.Unchanged,
                ProcessingMode.ValidateOnly)
                => "Already normalized",

            (NormalizeResult.Unchanged, _)
                => "Unchanged",

            (NormalizeResult.EncodingNotDetected, _)
                => "Skipped (unknown encoding)",

            _ => throw new ArgumentOutOfRangeException(
                nameof(result))
        };
    }

    /// <summary>
    /// Unchanged files are shown only with -Verbose.
    /// </summary>
    private static bool GetConsoleVisibility(
        NormalizeResult result,
        Options options)
    {
        return result switch
        {
            NormalizeResult.Unchanged =>
                options is { Verbose: true, Quiet: false },

            _ =>
                !options.Quiet
        };
    }

    /// <summary>
    /// Emits one result while keeping parallel console output intact.
    /// </summary>
    private static void EmitConsole(
        FileOutcome outcome,
        ProcessingMode mode)
    {
        if (outcome.IsError)
        {
            lock (ConsoleLock)
            {
                Console.ForegroundColor =
                    ConsoleColor.Red;

                Console.Error.WriteLine(
                    outcome.DisplayPath);

                Console.Error.WriteLine(
                    "    " + outcome.ErrorMessage);

                Console.ResetColor();
            }

            return;
        }

        if (!outcome.ConsoleVisible)
        {
            return;
        }

        lock (ConsoleLock)
        {
            switch (outcome.Result)
            {
                case NormalizeResult.Unchanged:

                    Console.WriteLine(
                        "OK     {0}",
                        outcome.DisplayPath);

                    break;

                case NormalizeResult.EncodingNotDetected:

                    Console.ForegroundColor =
                        ConsoleColor.DarkYellow;

                    Console.WriteLine(
                        "{0}: {1}",
                        outcome.ResultLabel,
                        outcome.DisplayPath);

                    Console.ResetColor();

                    break;

                case NormalizeResult.Converted:

                    Console.ForegroundColor =
                        mode == ProcessingMode.Normalize
                            ? ConsoleColor.Green
                            : ConsoleColor.Yellow;

                    Console.WriteLine(
                        "{0}: {1}",
                        outcome.ResultLabel,
                        outcome.DisplayPath);

                    Console.ResetColor();

                    break;
            }
        }
    }

    /// <summary>
    /// Processes files with bounded parallelism.
    /// </summary>
    private static bool RunConvertOrValidate(
        Options options,
        Statistics statistics,
        ProcessingMode mode,
        CancellationToken cancellationToken)
    {
        bool wantsReport = options.Report != null;

        FileOutcome[]? outcomes = ProcessFiles(
            EnumerateCandidates(options),
            options,
            cancellationToken,
            collectResults: wantsReport,
            process: file =>
                ComputeFileOutcome(
                    file,
                    options,
                    mode,
                    statistics,
                    cancellationToken),
            emit: outcome =>
                EmitConsole(
                    outcome,
                    mode),
            sortKey: outcome => outcome.SortKey);

        if (!wantsReport)
        {
            return true;
        }

        List<string> rows =
            [..
                outcomes!.Select(
                    outcome =>
                        BuildNormalizeCsvRow(
                            outcome,
                            options))];

        if (TryWriteReportFile(
                options.Report!,
                "File,Encoding,BOM,LineEnding,Target,Result,ReasonCode,Diagnostic",
                rows,
                out string? error))
        {
            Console.WriteLine(
                "Report written: {0}",
                Path.GetFullPath(
                    options.Report!));

            return true;
        }

        Console.Error.WriteLine(
            "Error writing report: {0}",
            options.Report);

        Console.Error.WriteLine(
            "    " + error);

        return false;
    }

    /// <summary>
    /// Builds a stable normalization report row.
    /// </summary>
    private static string BuildNormalizeCsvRow(
        FileOutcome outcome,
        Options options)
    {
        (string encoding,
            string bom,
            string lineEnding) =
            outcome.Detected != null
                ? BuildDetectResultColumns(
                    outcome.Detected)
                : ("", "", "");

        string target =
            options.TargetLineEnding
                .ToString()
                .ToUpperInvariant();

        return string.Join(
            ",",
            EscapeCsvField(outcome.DisplayPath),
            EscapeCsvField(encoding),
            bom,
            lineEnding,
            target,
            EscapeCsvField(outcome.ResultLabel),
            EscapeCsvField(outcome.ReasonCode ?? ""),
            EscapeCsvField(outcome.ErrorMessage ?? ""));
    }

    #endregion

    /// <summary>
    /// Converts detection data to stable report values.
    /// </summary>
    private static (
        string Encoding,
        string Bom,
        string LineEnding)
        BuildDetectResultColumns(
            DetectResult result)
    {
        return (
            GetEncodingDisplayName(
                result.Encoding),

            result.HasBom
                ? "Yes"
                : "No",

            GetLineEndingKindDisplayName(
                result.LineEndingKind));
    }

    /// <summary>
    /// Creates bounded parallel-processing options.
    /// </summary>
    internal static ParallelOptions CreateParallelOptions(
        Options options,
        CancellationToken cancellationToken = default)
    {
        int maxDegreeOfParallelism =
            options.MaxParallelism ??
            Math.Max(
                1,
                Math.Min(
                    Environment.ProcessorCount,
                    DefaultMaxParallelism));

        return new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism =
                maxDegreeOfParallelism
        };
    }

    /// <summary>
    /// Candidate enumeration shared by Convert/Validate/WhatIf and DetectOnly.
    /// </summary>
    private static IEnumerable<string> EnumerateCandidates(
        Options options)
    {
        var includePatterns =
            FilePatternMatcher.Compile(
                options.IncludePatterns);

        List<Regex>? excludePatterns =
            options.ExcludePatterns.Count > 0
                ? FilePatternMatcher.Compile(
                    options.ExcludePatterns)
                : null;

        return DirectoryTraversal.EnumerateCandidateFiles(
                options.BasePath ?? "",
                onWarning: PrintTraversalWarning,
                excludedFullPath: options.Report is null
                    ? null
                    : Path.GetFullPath(options.Report))
            .Where(file =>
                DirectoryTraversal.IsCandidateFile(
                    Path.GetRelativePath(
                        options.BasePath ?? "",
                        file).Replace('\\', '/'),
                    includePatterns,
                    excludePatterns));
    }

    /// <summary>
    /// Deterministic-vs-parallel dispatch shared by Convert/Validate/WhatIf and
    /// DetectOnly. Per-file computation and result semantics stay with the caller.
    /// Never catches or transforms exceptions from <paramref name="process"/>:
    /// per-file errors remain the caller's responsibility, and cancellation
    /// still propagates uncaught through Parallel.For/ForEach.
    /// </summary>
    private static TOutcome[]? ProcessFiles<TOutcome>(
        IEnumerable<string> candidateFiles,
        Options options,
        CancellationToken cancellationToken,
        bool collectResults,
        Func<string, TOutcome> process,
        Action<TOutcome> emit,
        Func<TOutcome, string> sortKey)
    {
        if (options.Deterministic)
        {
            List<string> files =
                [.. candidateFiles];

            files.Sort(
                StringComparer.Ordinal);

            var outcomes =
                new TOutcome[files.Count];

            Parallel.For(
                0,
                files.Count,
                CreateParallelOptions(
                    options,
                    cancellationToken),
                i =>
                {
                    outcomes[i] =
                        process(files[i]);
                });

            foreach (var outcome in outcomes)
            {
                emit(outcome);
            }

            return collectResults ? outcomes : null;
        }

        List<TOutcome>? collected =
            collectResults ? [] : null;

        var collectedLock = new Lock();

        Parallel.ForEach(
            candidateFiles,
            CreateParallelOptions(
                options,
                cancellationToken),
            file =>
            {
                TOutcome outcome =
                    process(file);

                emit(outcome);

                if (collectResults)
                {
                    lock (collectedLock)
                    {
                        collected!.Add(
                            outcome);
                    }
                }
            });

        collected?.Sort(
            (a, b) =>
                string.CompareOrdinal(
                    sortKey(a),
                    sortKey(b)));

        return collected?.ToArray();
    }

    #region -DetectOnly

    /// <summary>
    /// Captures read-only detection results.
    /// </summary>
    private sealed record DetectOutcome(
        string DisplayPath,
        string SortKey,
        DetectResult? Detected,
        bool IsError,
        string? Message);

    /// <summary>
    /// Performs read-only detection for one file.
    /// </summary>
    private static DetectOutcome ComputeDetectOutcome(
        string file,
        Options options,
        CancellationToken cancellationToken)
    {
        string displayPath =
            FormatDisplayPath(
                file,
                options);

        try
        {
            DetectResult? result =
                NewLineNormalizer.DetectFile(
                    file,
                    cancellationToken);

            return new DetectOutcome(
                displayPath,
                file,
                result,
                IsError: false,
                Message: null);
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException)
        {
            return new DetectOutcome(
                displayPath,
                file,
                Detected: null,
                IsError: true,
                Message: ex.Message);
        }
    }

    /// <summary>
    /// Builds one -DetectOnly CSV row.
    /// </summary>
    private static string BuildDetectCsvRow(
        string displayPath,
        DetectResult result)
    {
        (string encoding,
            string bom,
            string lineEnding) =
            BuildDetectResultColumns(
                result);

        return string.Join(
            ",",
            lineEnding,
            encoding,
            bom,
            EscapeCsvField(displayPath));
    }

    /// <summary>
    /// Emits one detection result.
    /// </summary>
    private static void EmitDetectConsole(
        DetectOutcome outcome)
    {
        if (outcome.IsError)
        {
            lock (ConsoleLock)
            {
                Console.Error.WriteLine(
                    "Error scanning: {0}",
                    outcome.DisplayPath);

                Console.Error.WriteLine(
                    "    " + outcome.Message);
            }

            return;
        }

        if (outcome.Detected == null)
        {
            lock (ConsoleLock)
            {
                Console.Error.WriteLine(
                    "Skipped (unknown encoding): {0}",
                    outcome.DisplayPath);
            }

            return;
        }

        lock (ConsoleLock)
        {
            Console.WriteLine(
                BuildDetectCsvRow(
                    outcome.DisplayPath,
                    outcome.Detected));
        }
    }

    /// <summary>
    /// Runs strictly read-only detection; stdout remains CSV.
    /// </summary>
    private static int RunDetectOnly(
        Options options,
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            "LineEnding,Encoding,BOM,File");

        bool wantsReport = options.Report != null;
        int errorCount = 0;

        DetectOutcome[]? outcomes = ProcessFiles(
            EnumerateCandidates(options),
            options,
            cancellationToken,
            collectResults: wantsReport,
            process: file =>
                ComputeDetectOutcome(
                    file,
                    options,
                    cancellationToken),
            emit: outcome =>
            {
                if (outcome.IsError)
                {
                    Interlocked.Increment(
                        ref errorCount);
                }

                EmitDetectConsole(
                    outcome);
            },
            sortKey: outcome => outcome.SortKey);

        if (wantsReport)
        {
            List<string> rows =
                [..
                    outcomes!
                        .Where(o =>
                            o.Detected != null)
                        .Select(o =>
                            BuildDetectCsvRow(
                                o.DisplayPath,
                                o.Detected!))];

            if (TryWriteReportFile(
                    options.Report!,
                    "LineEnding,Encoding,BOM,File",
                    rows,
                    out string? error))
            {
                Console.Error.WriteLine(
                    "Report written: {0}",
                    Path.GetFullPath(
                        options.Report!));
            }
            else
            {
                Console.Error.WriteLine(
                    "Error writing report: {0}",
                    options.Report);

                Console.Error.WriteLine(
                    "    " + error);

                return ExitProcessingErrors;
            }
        }

        return errorCount == 0
            ? ExitSuccess
            : ExitProcessingErrors;
    }

    #endregion

    /// <summary>
    /// Writes UTF-8 CSV with stable CRLF row terminators.
    /// </summary>
    private static bool TryWriteReportFile(
        string path,
        string header,
        IReadOnlyList<string> rows,
        out string? errorMessage)
    {
        try
        {
            using var writer =
                new StreamWriter(
                    path,
                    append: false,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false));

            writer.NewLine = "\r\n";

            writer.WriteLine(header);

            foreach (var row in rows)
            {
                writer.WriteLine(row);
            }

            errorMessage = null;

            return true;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            errorMessage = ex.Message;

            return false;
        }
    }

    /// <summary>
    /// Maps line-ending kinds to stable CSV names.
    /// </summary>
    internal static string GetLineEndingKindDisplayName(
        LineEndingKind kind)
    {
        return kind switch
        {
            LineEndingKind.Crlf => "CRLF",
            LineEndingKind.Lf => "LF",
            LineEndingKind.Cr => "CR",
            LineEndingKind.Mixed => "MIXED",
            LineEndingKind.None => "NONE",

            _ => throw new ArgumentOutOfRangeException(
                nameof(kind))
        };
    }

    /// <summary>
    /// Maps detected encodings to stable display names.
    /// </summary>
    internal static string GetEncodingDisplayName(
        Encoding encoding)
    {
        if (encoding.Equals(
                UnicodeDetector.Utf8Bom) ||
            encoding.Equals(
                UnicodeDetector.Utf8NoBom))
        {
            return "UTF-8";
        }

        if (encoding.Equals(
                UnicodeDetector.Utf16LittleEndianBom) ||
            encoding.Equals(
                UnicodeDetector.Utf16LittleEndianNoBom))
        {
            return "UTF-16LE";
        }

        if (encoding.Equals(
                UnicodeDetector.Utf16BigEndianBom) ||
            encoding.Equals(
                UnicodeDetector.Utf16BigEndianNoBom))
        {
            return "UTF-16BE";
        }

        if (encoding.Equals(
                UnicodeDetector.Utf32LittleEndianBom) ||
            encoding.Equals(
                UnicodeDetector.Utf32LittleEndianNoBom))
        {
            return "UTF-32LE";
        }

        if (encoding.Equals(
                UnicodeDetector.Utf32BigEndianBom) ||
            encoding.Equals(
                UnicodeDetector.Utf32BigEndianNoBom))
        {
            return "UTF-32BE";
        }

        if (Equals(
                encoding,
                Encoding.ASCII))
        {
            return "ASCII";
        }

        return encoding.WebName;
    }

    /// <summary>
    /// Escapes a CSV field according to RFC 4180.
    /// </summary>
    internal static string EscapeCsvField(
        string field)
    {
        if (field.IndexOfAny(
                [
                    ',',
                    '"',
                    '\r',
                    '\n'
                ]) < 0)
        {
            return field;
        }

        return "\"" +
               field.Replace(
                   "\"",
                   "\"\"") +
               "\"";
    }

    /// <summary>
    /// Formats output paths without changing file-operation paths.
    /// </summary>
    internal static string FormatDisplayPath(
        string file,
        Options options)
    {
        if (options.FullPath)
        {
            return Path.GetFullPath(file);
        }

        return Path.GetRelativePath(
            options.BasePath ?? file,
            file);
    }

    /// <summary>
    /// Prints final processing statistics.
    /// </summary>
    private static void PrintSummary(
        Statistics statistics,
        ProcessingMode mode)
    {
        Console.ForegroundColor =
            ConsoleColor.Cyan;

        Console.WriteLine(
            mode == ProcessingMode.ValidateOnly
                ? "Validation complete."
                : "Scan complete.");

        Console.ResetColor();

        Console.WriteLine(
            "{0,-19}: {1,8}",
            "Files scanned",
            statistics.Checked);

        if (mode == ProcessingMode.ValidateOnly)
        {
            Console.WriteLine(
                "{0,-19}: {1,8}",
                "Already normalized",
                statistics.Unchanged);

            Console.ForegroundColor =
                ConsoleColor.Yellow;

            Console.WriteLine(
                "{0,-19}: {1,8}",
                "Invalid",
                statistics.Converted);

            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor =
                mode == ProcessingMode.WhatIf
                    ? ConsoleColor.Yellow
                    : ConsoleColor.Green;

            Console.WriteLine(
                "{0,-19}: {1,8}",
                mode == ProcessingMode.WhatIf
                    ? "Would convert"
                    : "Converted",
                statistics.Converted);

            Console.ResetColor();

            Console.WriteLine(
                "{0,-19}: {1,8}",
                "Already normalized",
                statistics.Unchanged);
        }

        Console.ForegroundColor =
            ConsoleColor.DarkYellow;

        Console.WriteLine(
            "{0,-19}: {1,8}",
            "Skipped",
            statistics.Skipped);

        Console.ResetColor();

        // Shown only when it happened, so the usual summary does not grow a
        // line that is always zero and stops being read.
        if (statistics.Refused > 0)
        {
            Console.ForegroundColor =
                ConsoleColor.Yellow;

            Console.WriteLine(
                "{0,-19}: {1,8}",
                "Refused",
                statistics.Refused);

            Console.ResetColor();
        }

        Console.WriteLine(
            "{0,-19}: {1,8}",
            "Failed",
            statistics.Errors);
    }

    /// <summary>
    /// Returns the product version generated from the project version.
    /// </summary>
    internal static string GetDisplayVersion()
    {
        Version version =
            typeof(Program).Assembly.GetName().Version ??
            new Version(0, 0, 0);

        return $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }


    private static void PrintUsage()
    {
        Console.WriteLine(
            "LineEndingNormalizer v{0}",
            GetDisplayVersion());

        Console.WriteLine();

        Console.WriteLine(
            """
            Usage:

              LineEndingNormalizer.exe --version
                       Print the version and exit. Needs no other argument.

              LineEndingNormalizer.exe
                  -BasePath <directory>
                  [-Include "<pattern1,pattern2,...>"]
                  [-Exclude "<pattern1,pattern2,...>"]
                       Wildcard patterns. A pattern with no "/" or "\"
                       matches just the filename, at any depth. One
                       containing a separator matches the path relative
                       to -BasePath instead (e.g. "src/*.cs"); "/" and
                       "\" behave the same way. Exclusions are applied
                       after -Include. The following directories are always
                       skipped: .git, .svn, .hg, .vs, .idea, bin, obj,
                       node_modules, packages, dist, build, target.
                       Linked files/directories, .bak files, and LEN's
                       temporary files are also skipped.

                  Normalization:
                  [-Target <CRLF|LF|CR>]
                       Target line-ending style. WINDOWS/UNIX/MAC are
                       accepted as aliases. Default: CRLF.

                  Modes:
                       Normalization is the default mode.
                  [-ValidateOnly]
                       Validate without writing anything. Files requiring
                       conversion are reported as Invalid. May be combined
                       with -FailOnChanges. Cannot be combined with
                       -DetectOnly, -WhatIf, or -Backup.
                  [-DetectOnly]
                       Read-only detection mode. Writes
                       LineEnding,Encoding,BOM,File CSV rows to stdout.
                       Nothing is modified. Cannot be combined with
                       -ValidateOnly, -Target, -WhatIf, -Backup,
                       -FailOnChanges, -Quiet, or -Verbose.

                  Report:
                  [-Report <path>]
                       Write a CSV report in addition to normal console
                       output. Valid in every mode. Detection data is
                       collected before normalization so the report
                       describes the original file. Failed rows include a
                       stable ReasonCode and a detailed Diagnostic.

                  Performance:
                  [-MaxParallelism <N>]
                       Maximum number of files processed concurrently.
                       Default: min(logical processor count, 4).

                  Dry run / safety:
                  [-WhatIf]
                       Convert mode only. Computes what each file's result
                       would be without writing anything.
                  [-Backup]
                       Convert mode only. Before overwriting a file, copies
                       the original to "<file>.bak" (overwriting any
                       previous one) and verifies the backup hash.
                       Cannot be combined with -WhatIf or -ValidateOnly.

                  Output:
                  [-Verbose]
                       Show every checked file, including unchanged files.
                  [-Quiet]
                       Suppress per-file console output. Summary and errors
                       remain visible. Does not affect -Report.
                  [-FullPath]
                       Use absolute paths instead of paths relative to
                       -BasePath. Applies equally to console and CSV output.
                  [-Deterministic]
                       Process and emit console results in ordinal lexical
                       path order. Report rows are always sorted.

                  CI / Exit Code:
                  [-FailOnChanges]
                       Exit with a non-zero code when any file requires (or,
                       under -ValidateOnly, fails) conversion. Useful as a
                       CI gate with -WhatIf or -ValidateOnly.

            Exit codes: 0 = clean; 1 = usage/argument error, including a
            missing -BasePath directory; 2 = -FailOnChanges triggered; 3 = one
            or more files failed to process, or the -Report file could not be
            written; 4 = cancelled (Ctrl+C); 5 = one or more files were safely
            refused and left unchanged; 6 = -BasePath is a reparse point.
            Codes 0-5 match EncodingChecker's. When several apply the first of
            3, 5, 2, 0 wins.

            Examples:

              LineEndingNormalizer.exe ^
                  -BasePath C:\Source ^
                  -Include "*.cs,*.txt" ^
                  -Target CRLF

              LineEndingNormalizer.exe ^
                  -BasePath . ^
                  -Include "*.cpp,*.hpp" ^
                  -Target LF ^
                  -WhatIf

              LineEndingNormalizer.exe ^
                  -BasePath . ^
                  -Include "*.cs" ^
                  -Exclude "*.designer.cs,*.g.cs" ^
                  -Target CRLF

              LineEndingNormalizer.exe ^
                  -BasePath . ^
                  -Include "*" ^
                  -Target CRLF ^
                  -WhatIf ^
                  -FailOnChanges ^
                  -Quiet

              LineEndingNormalizer.exe ^
                  -BasePath . ^
                  -Include "*.cs,*.txt" ^
                  -Target CRLF ^
                  -Backup

              LineEndingNormalizer.exe ^
                  -BasePath . ^
                  -Include "*" ^
                  -DetectOnly ^
                  > report.csv

              LineEndingNormalizer.exe ^
                  -BasePath D:\NetworkShare ^
                  -Include "*.txt" ^
                  -Target CRLF ^
                  -MaxParallelism 2

              LineEndingNormalizer.exe ^
                  -BasePath . ^
                  -Include "*" ^
                  -DetectOnly ^
                  -FullPath ^
                  > report.csv

              LineEndingNormalizer.exe ^
                  -BasePath . ^
                  -Include "*" ^
                  -Target CRLF ^
                  -ValidateOnly ^
                  -FailOnChanges ^
                  -Quiet

              LineEndingNormalizer.exe ^
                  -BasePath . ^
                  -Include "*" ^
                  -Target CRLF ^
                  -Report report.csv ^
                  -Deterministic

            """);
    }
}

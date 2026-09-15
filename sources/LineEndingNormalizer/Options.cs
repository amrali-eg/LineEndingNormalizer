namespace LineEndingNormalizer;

/// <summary>
/// Command-line options.
/// </summary>
internal sealed class Options
{
    /// <summary>
    /// Root directory to scan recursively.
    /// </summary>
    public string? BasePath { get; set; }

    /// <summary>
    /// File name patterns to include.
    /// </summary>
    public List<string> IncludePatterns { get; private set; } = [];

    /// <summary>
    /// File name patterns to exclude.
    /// </summary>
    public List<string> ExcludePatterns { get; private set; } = [];

    /// <summary>
    /// Target line-ending style.
    /// </summary>
    public LineEnding TargetLineEnding { get; set; } = LineEnding.Crlf;

    /// <summary>
    /// Display every processed file.
    /// </summary>
    public bool Verbose { get; set; }

    /// <summary>
    /// Dry-run mode; reports files that would be converted without modifying them.
    /// </summary>
    public bool WhatIf { get; set; }

    /// <summary>
    /// Return a non-zero exit code if any file needs conversion.
    /// </summary>
    public bool FailOnChanges { get; set; }

    /// <summary>
    /// Suppress per-file console output.
    /// </summary>
    public bool Quiet { get; set; }

    /// <summary>
    /// Create a backup before replacing each file.
    /// </summary>
    public bool Backup { get; set; }

    /// <summary>
    /// Detect and report encoding, BOM, and line-ending information without modifying files.
    /// </summary>
    public bool DetectOnly { get; set; }

    /// <summary>
    /// Maximum number of files processed concurrently.
    /// </summary>
    public int? MaxParallelism { get; set; }

    /// <summary>
    /// Display absolute paths instead of paths relative to <see cref="BasePath"/>.
    /// </summary>
    public bool FullPath { get; set; }

    /// <summary>
    /// Process and report files in deterministic ordinal path order on the console.
    /// CSV reports are always sorted independently of this option.
    /// </summary>
    public bool Deterministic { get; set; }

    /// <summary>
    /// Write a CSV report to the specified path.
    /// </summary>
    public string? Report { get; set; }

    /// <summary>
    /// Validate files without modifying them; reports files that do not
    /// match <see cref="TargetLineEnding"/>.
    /// </summary>
    public bool ValidateOnly { get; set; }
}

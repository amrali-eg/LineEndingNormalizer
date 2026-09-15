namespace LineEndingNormalizer;

/// <summary>
/// Detects encodings and normalizes line endings while preserving
/// encoding and file metadata.
/// </summary>
internal static class NewLineNormalizer
{
    private const int BufferSize = 65536;

    #region Public API

    /// <summary>
    /// Normalizes a file's line endings while preserving encoding and BOM.
    /// </summary>
    public static NormalizeResult NormalizeFile(
        string path,
        LineEnding target,
        bool whatIf,
        bool backup = false,
        CancellationToken cancellationToken = default)
    {
        return NormalizeFile(
            path,
            target,
            whatIf,
            backup,
            out _,
            cancellationToken);
    }

    /// <summary>
    /// Normalizes a file and optionally returns its original detection result.
    /// </summary>
    /// <param name="detected">
    /// Null when encoding detection fails; otherwise the detection result from
    /// the same scan used for the normalization decision.
    /// </param>
    internal static NormalizeResult NormalizeFile(
        string path,
        LineEnding target,
        bool whatIf,
        bool backup,
        out DetectResult? detected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "File not found.",
                path);
        }

        // Keep one source stream through scan and conversion: this avoids a
        // redundant open and narrows the window for an external change
        // between detection and the write that follows it.
        using FileStream source =
            OpenSourceStream(path);

        ScanEngine.ScanResult? scan =
            ScanEngine.Scan(
                source,
                target,
                cancellationToken);

        detected =
            scan?.Detection;

        if (scan == null)
        {
            return NormalizeResult.EncodingNotDetected;
        }

        if (!scan.RequiresConversion)
        {
            return NormalizeResult.Unchanged;
        }

        // A BOM-less UTF-16 guess is not safe to rewrite when the opposite
        // byte order also strictly accepts the complete file.
        BomlessUnicodeSafety.EnsureSafeToNormalize(
            source,
            scan.Detection,
            cancellationToken);

        if (whatIf)
        {
            return NormalizeResult.Converted;
        }

        FileMetadata metadata =
            CaptureMetadata(path);

        // Conversion and replacement safeguards remain in ConvertFile.
        LosslessFileWriter.ConvertFile(
            source,
            path,
            scan.Detection.Encoding,
            target,
            metadata,
            backup,
            cancellationToken);

        return NormalizeResult.Converted;
    }


    /// <summary>
    /// Detects encoding, BOM state, and line-ending style without modifying the file.
    /// </summary>
    /// <returns>
    /// The detection result, or <see langword="null"/> if encoding is unknown.
    /// </returns>
    /// <exception cref="DecoderFallbackException">
    /// The file fails strict decoding.
    /// </exception>
    public static DetectResult? DetectFile(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(path);

        using FileStream source =
            OpenSourceStream(path);

        // No target: only detection and classification are needed.
        ScanEngine.ScanResult? scan =
            ScanEngine.Scan(
                source,
                target: null,
                cancellationToken);

        return scan?.Detection;
    }

    #endregion


    #region Source Opening

    /// <summary>
    /// Opens a source for read-only sequential processing.
    /// </summary>
    private static FileStream OpenSourceStream(
        string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.SequentialScan);
    }

    #endregion


    #region Metadata Capture

    /// <summary>
    /// Captures metadata for preservation during replacement.
    /// </summary>
    private static FileMetadata CaptureMetadata(
        string path)
    {
        var info =
            new FileInfo(path);

        return new FileMetadata(
            info.Attributes,
            info.Length,
            info.CreationTimeUtc,
            info.LastWriteTimeUtc,
            info.LastAccessTimeUtc);
    }

    #endregion
}

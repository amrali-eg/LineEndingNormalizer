namespace LineEndingNormalizer;

/// <summary>
/// Result of processing one file.
/// </summary>
internal enum NormalizeResult
{
    /// <summary>No rewrite was needed.</summary>
    Unchanged,

    /// <summary>The file was or would be rewritten.</summary>
    Converted,

    /// <summary>Encoding was unknown; conversion was skipped.</summary>
    EncodingNotDetected
}

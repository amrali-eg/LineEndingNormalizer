namespace LineEndingNormalizer;

/// <summary>
/// Processing mode for files; <c>-DetectOnly</c> is handled separately.
/// </summary>
internal enum ProcessingMode
{
    /// <summary>Rewrite files that need conversion.</summary>
    Normalize,

    /// <summary>Report files that would be converted.</summary>
    WhatIf,

    /// <summary>Report files that are not already normalized.</summary>
    ValidateOnly
}

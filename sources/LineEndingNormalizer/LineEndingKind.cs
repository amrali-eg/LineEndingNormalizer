namespace LineEndingNormalizer;

/// <summary>
/// Line-ending style observed during detection.
/// </summary>
internal enum LineEndingKind
{
    /// <summary>All line breaks are CRLF.</summary>
    Crlf,

    /// <summary>All line breaks are LF.</summary>
    Lf,

    /// <summary>All line breaks are CR.</summary>
    Cr,

    /// <summary>Multiple line-ending styles are present.</summary>
    Mixed,

    /// <summary>No line breaks were found.</summary>
    None
}

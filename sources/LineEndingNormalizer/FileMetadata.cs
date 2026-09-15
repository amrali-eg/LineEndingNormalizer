namespace LineEndingNormalizer;

/// <summary>
/// Immutable file metadata captured before conversion and restored to the
/// replacement file. Also used to detect changes before replacement.
/// </summary>
/// <remarks>
/// Timestamps are UTC so the pre-replacement revalidation comparison isn't
/// affected by DST transitions between capture and replacement.
/// </remarks>
internal sealed record FileMetadata(
    FileAttributes Attributes,
    long Length,
    DateTime CreationTimeUtc,
    DateTime LastWriteTimeUtc,
    DateTime LastAccessTimeUtc);

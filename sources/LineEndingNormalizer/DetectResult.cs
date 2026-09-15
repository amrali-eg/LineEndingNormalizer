using System.Text;

namespace LineEndingNormalizer;

/// <summary>
/// Detected encoding, BOM state, and line-ending style.
/// </summary>
internal sealed record DetectResult(
    Encoding Encoding,
    bool HasBom,
    LineEndingKind LineEndingKind);

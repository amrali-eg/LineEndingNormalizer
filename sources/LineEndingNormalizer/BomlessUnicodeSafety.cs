using System.Buffers;
using System.Text;

namespace LineEndingNormalizer;

/// <summary>
/// Prevents destructive normalization when BOM-less UTF-16 or UTF-32 byte
/// order cannot be established from the bytes alone.
/// </summary>
internal static class BomlessUnicodeSafety
{
    private const int BufferSize = 65536;

    internal const string AmbiguousReasonCode =
        "AmbiguousBomlessUtf16";

    internal const string AmbiguousUtf32ReasonCode =
        "AmbiguousBomlessUtf32";

    // Both families use adjacent .NET code pages for the little- and
    // big-endian variant (1200/1201, 12000/12001), so one offset works for
    // either.
    private const int BigEndianOffset = 1;

    /// <summary>
    /// Refuses conversion when the same bytes are valid under the opposite
    /// UTF-16 or UTF-32 byte order. Detection may still report its preferred
    /// byte order, but that preference is not enough to justify rewriting
    /// the file.
    /// </summary>
    internal static void EnsureSafeToNormalize(
        Stream source,
        DetectResult detection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(detection);

        if (detection.HasBom)
        {
            return;
        }

        switch (detection.Encoding.CodePage)
        {
            case 1200 or 1201:
                EnsureSafeAgainstOppositeByteOrder(
                    source,
                    detection,
                    cancellationToken,
                    littleEndianCodePage: 1200,
                    familyName: "UTF-16",
                    reasonCode: AmbiguousReasonCode);
                return;

            case 12000 or 12001:
                EnsureSafeAgainstOppositeByteOrder(
                    source,
                    detection,
                    cancellationToken,
                    littleEndianCodePage: 12000,
                    familyName: "UTF-32",
                    reasonCode: AmbiguousUtf32ReasonCode);
                return;
        }
    }

    private static void EnsureSafeAgainstOppositeByteOrder(
        Stream source,
        DetectResult detection,
        CancellationToken cancellationToken,
        int littleEndianCodePage,
        string familyName,
        string reasonCode)
    {
        int bigEndianCodePage =
            littleEndianCodePage + BigEndianOffset;

        int oppositeCodePage =
            detection.Encoding.CodePage == littleEndianCodePage
                ? bigEndianCodePage
                : littleEndianCodePage;

        if (!CanDecodeStrictly(
                source,
                oppositeCodePage,
                cancellationToken))
        {
            return;
        }

        string detectedName =
            detection.Encoding.CodePage == littleEndianCodePage
                ? $"{familyName}LE"
                : $"{familyName}BE";

        string oppositeName =
            oppositeCodePage == littleEndianCodePage
                ? $"{familyName}LE"
                : $"{familyName}BE";

        throw new ConversionRefusedException(
            reasonCode,
            $"Refusing to normalize BOM-less {familyName} because the bytes are valid as both {detectedName} and {oppositeName}. " +
            "Add a byte-order mark or use a tool that lets you explicitly confirm the source byte order.");
    }

    private static bool CanDecodeStrictly(
        Stream source,
        int codePage,
        CancellationToken cancellationToken)
    {
        long originalPosition = source.Position;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(BufferSize);
        char[] chars = ArrayPool<char>.Shared.Rent(BufferSize);

        try
        {
            source.Position = 0;

            Decoder decoder =
                Encoding.GetEncoding(
                        codePage,
                        EncoderFallback.ExceptionFallback,
                        DecoderFallback.ExceptionFallback)
                    .GetDecoder();

            int read;

            while ((read = source.Read(bytes, 0, BufferSize)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int offset = 0;

                // Convert reports completed=false when the char buffer could not
                // take the whole chunk, and it does not retain the unconsumed
                // bytes. Discarding that flag would let this method judge a file
                // decodable from a prefix of it - and this decision is what
                // stands between an ambiguous file and being rewritten. The
                // buffers are sized so it cannot happen today; the loop makes
                // that safe rather than assumed.
                while (offset < read)
                {
                    decoder.Convert(
                        bytes,
                        offset,
                        read - offset,
                        chars,
                        0,
                        chars.Length,
                        flush: false,
                        out int bytesUsed,
                        out _,
                        out bool completed);

                    if (bytesUsed == 0 && !completed)
                    {
                        // No forward progress is possible; refuse to call this
                        // a successful decode rather than spin.
                        return false;
                    }

                    offset += bytesUsed;
                }
            }

            decoder.Convert(
                [],
                0,
                0,
                chars,
                0,
                chars.Length,
                flush: true,
                out _,
                out _,
                out bool flushed);

            // A trailing incomplete code unit leaves the decoder unfinished.
            return flushed;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        finally
        {
            source.Position = originalPosition;
            ArrayPool<byte>.Shared.Return(bytes);
            ArrayPool<char>.Shared.Return(chars);
        }
    }
}

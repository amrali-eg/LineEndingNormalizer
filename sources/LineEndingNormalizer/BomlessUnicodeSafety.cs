using System.Buffers;
using System.Text;

namespace LineEndingNormalizer;

/// <summary>
/// Prevents destructive normalization when a BOM-less Unicode file's codec or
/// byte order cannot be established from the bytes alone.
/// </summary>
internal static class BomlessUnicodeSafety
{
    private const int BufferSize = 65536;

    internal const string AmbiguousReasonCode =
        "AmbiguousBomlessUtf16";

    internal const string UnprovableUtf32ReasonCode =
        "UnprovableBomlessUtf32";

    /// <summary>
    /// Refuses conversion when a BOM-less file's codec or byte order is not
    /// provable from its bytes.
    /// </summary>
    /// <remarks>
    /// UTF-16 is refused only when the opposite byte order also strictly
    /// decodes the whole file, because otherwise the bytes do establish the
    /// order. Detection may still report its preferred byte order, but that
    /// preference is not enough to justify rewriting the file.
    ///
    /// UTF-32 is refused whenever no BOM is present; an opposite-order test
    /// would not do. A BOM-less UTF-16 file with one character per line puts
    /// a C0 control in every second code unit, so each resulting four-byte
    /// group is an in-range unassigned scalar and the file decodes as
    /// UTF-32LE, while the *opposite* UTF-32 order rejects it - an
    /// opposite-order test would wave that file through as unambiguous and
    /// rewrite it under the wrong codec entirely. What cannot be proven is
    /// the codec, not merely its byte order.
    /// </remarks>
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
            case 12000 or 12001:
                throw new ConversionRefusedException(
                    UnprovableUtf32ReasonCode,
                    "Refusing to normalize BOM-less UTF-32: the codec cannot be proven from " +
                    "the bytes alone. Add a byte-order mark to identify it.");

            case 1200 or 1201:
                EnsureUtf16ByteOrderIsUnambiguous(source, detection, cancellationToken);
                return;
        }
    }

    private static void EnsureUtf16ByteOrderIsUnambiguous(
        Stream source,
        DetectResult detection,
        CancellationToken cancellationToken)
    {
        const int LittleEndian = 1200;
        const int BigEndian = 1201;

        int oppositeCodePage =
            detection.Encoding.CodePage == LittleEndian
                ? BigEndian
                : LittleEndian;

        if (!CanDecodeStrictly(
                source,
                oppositeCodePage,
                cancellationToken))
        {
            return;
        }

        string detectedName =
            detection.Encoding.CodePage == LittleEndian
                ? "UTF-16LE"
                : "UTF-16BE";

        string oppositeName =
            oppositeCodePage == LittleEndian
                ? "UTF-16LE"
                : "UTF-16BE";

        throw new ConversionRefusedException(
            AmbiguousReasonCode,
            $"Refusing to normalize BOM-less UTF-16 because the bytes are valid as both {detectedName} and {oppositeName}. " +
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

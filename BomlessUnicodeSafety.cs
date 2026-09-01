using System.Buffers;
using System.Text;

namespace LineEndingNormalizer;

/// <summary>
/// Prevents destructive normalization when BOM-less UTF-16 byte order cannot
/// be established from the bytes alone.
/// </summary>
internal static class BomlessUnicodeSafety
{
    private const int BufferSize = 65536;

    internal const string AmbiguousReasonCode =
        "AmbiguousBomlessUtf16";

    /// <summary>
    /// Refuses conversion when the same bytes are valid under the opposite
    /// UTF-16 byte order. Detection may still report its preferred byte order,
    /// but that preference is not enough to justify rewriting the file.
    /// </summary>
    internal static void EnsureSafeToNormalize(
        Stream source,
        DetectResult detection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(detection);

        if (detection.HasBom ||
            detection.Encoding.CodePage is not (1200 or 1201))
        {
            return;
        }

        int oppositeCodePage =
            detection.Encoding.CodePage == 1200
                ? 1201
                : 1200;

        if (!CanDecodeStrictly(
                source,
                oppositeCodePage,
                cancellationToken))
        {
            return;
        }

        string detectedName =
            detection.Encoding.CodePage == 1200
                ? "UTF-16LE"
                : "UTF-16BE";

        string oppositeName =
            oppositeCodePage == 1200
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

                decoder.Convert(
                    bytes,
                    0,
                    read,
                    chars,
                    0,
                    chars.Length,
                    flush: false,
                    out _,
                    out _,
                    out _);
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
                out _);

            return true;
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

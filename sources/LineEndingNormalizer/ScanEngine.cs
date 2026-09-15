using System.Buffers;
using System.Text;

namespace LineEndingNormalizer;

/// <summary>
/// Shared read-only scan for detection and normalization decisions.
/// </summary>
internal static class ScanEngine
{
    private const int BufferSize = 65536;

    /// <summary>
    /// Detected file properties and normalization decision.
    /// </summary>
    internal sealed record ScanResult(
        DetectResult Detection,
        bool RequiresConversion);

    /// <summary>
    /// Detects encoding, line endings, and whether normalization is needed.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> if the encoding is unknown.
    /// </returns>
    internal static ScanResult? Scan(
        Stream stream,
        LineEnding? target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        cancellationToken.ThrowIfCancellationRequested();

        Encoding? encoding = TextEncoding.DetectFromStream(stream);

        if (encoding == null)
        {
            return null;
        }

        return TextEncoding.IsUnicodeEncoding(encoding)
            ? ScanUnicode(stream, encoding, target, cancellationToken)
            : ScanBytes(stream, encoding, target, cancellationToken);
    }

    /// <summary>
    /// Strictly decodes and scans Unicode input.
    /// </summary>
    private static ScanResult ScanUnicode(
        Stream stream,
        Encoding encoding,
        LineEnding? target,
        CancellationToken cancellationToken)
    {
        var decoder = encoding.GetDecoder();

        // Reject malformed Unicode instead of replacing it.
        decoder.Fallback = DecoderFallback.ExceptionFallback;

        byte[] bytes = ArrayPool<byte>.Shared.Rent(BufferSize);

        char[] chars = ArrayPool<char>.Shared.Rent(
            encoding.GetMaxCharCount(BufferSize));

        try
        {
            bool sawCrLf = false;
            bool sawLf = false;
            bool sawCr = false;
            bool pendingCr = false;

            // Unicode separators require conversion but are not classified.
            bool sawUnicodeLineSeparator = false;

            int read;

            while ((read = stream.Read(bytes, 0, BufferSize)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int count = decoder.GetChars(bytes, 0, read, chars, 0);

                for (int i = 0; i < count; i++)
                {
                    char c = chars[i];

                    if (pendingCr)
                    {
                        pendingCr = false;

                        if (c == '\n')
                        {
                            sawCrLf = true;
                            continue;
                        }

                        sawCr = true;
                    }

                    if (c == '\r')
                    {
                        pendingCr = true;
                    }
                    else if (c == '\n')
                    {
                        sawLf = true;
                    }
                    else if (c == '\u0085' || c == '\u2028' || c == '\u2029')
                    {
                        sawUnicodeLineSeparator = true;
                    }
                }
            }

            // Reject an incomplete trailing sequence.
            decoder.GetChars([], 0, 0, chars, 0, true);

            if (pendingCr)
            {
                sawCr = true;
            }

            LineEndingKind kind = Classify(sawCrLf, sawLf, sawCr);

            bool requiresConversion =
                target is LineEnding t &&
                (sawUnicodeLineSeparator ||
                 (sawCrLf && t != LineEnding.Crlf) ||
                 (sawLf && t != LineEnding.Lf) ||
                 (sawCr && t != LineEnding.Cr));

            return new ScanResult(
                new DetectResult(encoding, encoding.GetPreamble().Length > 0, kind),
                requiresConversion);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
            ArrayPool<char>.Shared.Return(chars);
        }
    }

    /// <summary>
    /// Scans legacy input as raw bytes.
    /// </summary>
    private static ScanResult ScanBytes(
        Stream stream,
        Encoding encoding,
        LineEnding? target,
        CancellationToken cancellationToken)
    {
        byte[] bytes = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            bool sawCrLf = false;
            bool sawLf = false;
            bool sawCr = false;
            bool pendingCr = false;

            int read;

            while ((read = stream.Read(bytes, 0, BufferSize)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                for (int i = 0; i < read; i++)
                {
                    byte b = bytes[i];

                    if (pendingCr)
                    {
                        pendingCr = false;

                        if (b == 0x0A)
                        {
                            sawCrLf = true;
                            continue;
                        }

                        sawCr = true;
                    }

                    if (b == 0x0D)
                    {
                        pendingCr = true;
                    }
                    else if (b == 0x0A)
                    {
                        sawLf = true;
                    }
                }
            }

            if (pendingCr)
            {
                sawCr = true;
            }

            LineEndingKind kind = Classify(sawCrLf, sawLf, sawCr);

            bool requiresConversion =
                target is LineEnding t &&
                ((sawCrLf && t != LineEnding.Crlf) ||
                 (sawLf && t != LineEnding.Lf) ||
                 (sawCr && t != LineEnding.Cr));

            return new ScanResult(
                new DetectResult(encoding, encoding.GetPreamble().Length > 0, kind),
                requiresConversion);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    /// <summary>
    /// Classifies the observed line-ending kinds.
    /// </summary>
    private static LineEndingKind Classify(
        bool sawCrLf,
        bool sawLf,
        bool sawCr)
    {
        int distinctKinds =
            (sawCrLf ? 1 : 0) +
            (sawLf ? 1 : 0) +
            (sawCr ? 1 : 0);

        if (distinctKinds == 0)
        {
            return LineEndingKind.None;
        }

        if (distinctKinds > 1)
        {
            return LineEndingKind.Mixed;
        }

        if (sawCrLf)
        {
            return LineEndingKind.Crlf;
        }

        return sawLf ? LineEndingKind.Lf : LineEndingKind.Cr;
    }
}

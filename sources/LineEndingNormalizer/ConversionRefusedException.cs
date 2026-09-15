namespace LineEndingNormalizer;

/// <summary>
/// Identifies a file that LEN deliberately left unchanged because conversion
/// could not be proven safe.
/// </summary>
internal sealed class ConversionRefusedException : IOException
{
    internal ConversionRefusedException(
        string reasonCode,
        string message)
        : base(message)
    {
        ReasonCode = reasonCode;
    }

    internal string ReasonCode { get; }
}

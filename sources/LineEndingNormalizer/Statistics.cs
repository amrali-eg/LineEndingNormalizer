namespace LineEndingNormalizer;

/// <summary>
/// Collects processing statistics.
///
/// All increments use <see cref="Interlocked"/> since files may be
/// processed concurrently.
/// </summary>
internal sealed class Statistics
{
    private int _checked;
    private int _converted;
    private int _unchanged;
    private int _skipped;
    private int _refused;
    private int _errors;

    /// <summary>
    /// Gets the number of matching files processed.
    /// </summary>
    public int Checked => _checked;

    /// <summary>
    /// Gets the number of modified files.
    /// </summary>
    public int Converted => _converted;

    /// <summary>
    /// Gets the number of unchanged files.
    /// </summary>
    public int Unchanged => _unchanged;

    /// <summary>
    /// Gets the number of files skipped because their encoding
    /// could not be determined.
    /// </summary>
    public int Skipped => _skipped;

    /// <summary>
    /// Gets the number of files LEN declined to convert because doing so could
    /// not be shown to be safe. A refusal is a correct outcome, not a failure.
    /// </summary>
    public int Refused => _refused;

    /// <summary>
    /// Gets the number of files that could not be processed.
    /// </summary>
    public int Errors => _errors;

    /// <summary>
    /// Increments the number of processed files.
    /// </summary>
    public void IncrementChecked()
    {
        Interlocked.Increment(ref _checked);
    }

    /// <summary>
    /// Increments the number of modified files.
    /// </summary>
    public void IncrementConverted()
    {
        Interlocked.Increment(ref _converted);
    }

    /// <summary>
    /// Increments the number of unchanged files.
    /// </summary>
    public void IncrementUnchanged()
    {
        Interlocked.Increment(ref _unchanged);
    }

    /// <summary>
    /// Increments the number of skipped files.
    /// </summary>
    public void IncrementSkipped()
    {
        Interlocked.Increment(ref _skipped);
    }

    /// <summary>
    /// Increments the number of safely refused files.
    /// </summary>
    public void IncrementRefused()
    {
        Interlocked.Increment(ref _refused);
    }

    /// <summary>
    /// Increments the number of failed files.
    /// </summary>
    public void IncrementErrors()
    {
        Interlocked.Increment(ref _errors);
    }
}

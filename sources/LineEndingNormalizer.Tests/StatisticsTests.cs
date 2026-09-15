namespace LineEndingNormalizer.Tests;

/// <summary>
/// Coverage for Statistics' Interlocked-based counters, which back the
/// parallel Program.ConvertDirectory processing.
/// </summary>
public sealed class StatisticsTests
{
    [Fact]
    public void ConcurrentIncrements_LoseNoUpdates()
    {
        var statistics = new Statistics();
        const int perCounter = 20000;

        Parallel.Invoke(
            () => IncrementMany(statistics.IncrementChecked, perCounter),
            () => IncrementMany(statistics.IncrementConverted, perCounter),
            () => IncrementMany(statistics.IncrementUnchanged, perCounter),
            () => IncrementMany(statistics.IncrementSkipped, perCounter),
            () => IncrementMany(statistics.IncrementErrors, perCounter),
            () => IncrementMany(statistics.IncrementChecked, perCounter),
            () => IncrementMany(statistics.IncrementConverted, perCounter),
            () => IncrementMany(statistics.IncrementUnchanged, perCounter));

        Assert.Equal(perCounter * 2, statistics.Checked);
        Assert.Equal(perCounter * 2, statistics.Converted);
        Assert.Equal(perCounter * 2, statistics.Unchanged);
        Assert.Equal(perCounter, statistics.Skipped);
        Assert.Equal(perCounter, statistics.Errors);
    }

    private static void IncrementMany(Action increment, int count)
    {
        for (int i = 0; i < count; i++)
        {
            increment();
        }
    }
}

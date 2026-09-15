namespace LineEndingNormalizer.Tests;

/// <summary>
/// Coverage for the bounded-parallelism default and its -MaxParallelism
/// override.
/// </summary>
public sealed class ParallelismTests
{
    [Fact]
    public void NoOverride_DefaultsToAtMostFour_AndNeverExceedsProcessorCount()
    {
        var options = new Options();

        int dop = Program.CreateParallelOptions(options).MaxDegreeOfParallelism;

        Assert.True(dop >= 1);
        Assert.True(dop <= 4);
        Assert.True(dop <= Environment.ProcessorCount);
    }

    [Fact]
    public void ExplicitOverride_IsUsedAsIs_EvenAboveTheDefaultCapOrProcessorCount()
    {
        var options = new Options { MaxParallelism = 64 };

        int dop = Program.CreateParallelOptions(options).MaxDegreeOfParallelism;

        // An explicit -MaxParallelism is the caller's own informed choice
        // (e.g. fast local NVMe, many tiny files) and is not silently
        // clamped back down to the conservative default or to
        // Environment.ProcessorCount.
        Assert.Equal(64, dop);
    }

    [Fact]
    public void ExplicitOverride_OfOne_DisablesParallelism()
    {
        var options = new Options { MaxParallelism = 1 };

        int dop = Program.CreateParallelOptions(options).MaxDegreeOfParallelism;

        Assert.Equal(1, dop);
    }
}

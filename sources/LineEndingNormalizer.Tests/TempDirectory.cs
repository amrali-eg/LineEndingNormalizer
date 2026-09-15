namespace LineEndingNormalizer.Tests;

/// <summary>
/// A uniquely-named temp directory that deletes itself (best-effort) on
/// dispose. Used to give each test an isolated filesystem sandbox.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "len-tests-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string CombinePath(string relative)
    {
        return System.IO.Path.Combine(Path, relative);
    }

    public string WriteFile(string relativePath, byte[] content)
    {
        string full = CombinePath(relativePath);

        string? dir = System.IO.Path.GetDirectoryName(full);

        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(full, content);

        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a lingering handle (e.g. antivirus scan)
            // shouldn't fail the test.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

namespace Imrdy.Integration.Tests.Helpers;

public sealed class TempDirectoryFixture : IDisposable
{
    public string Path { get; }

    public TempDirectoryFixture()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "imrdy-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
        Environment.SetEnvironmentVariable("IMRDY_HOME", Path);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("IMRDY_HOME", null);

        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[TempDirectoryFixture] Failed to clean up '{Path}': {ex.Message}");
        }
    }
}

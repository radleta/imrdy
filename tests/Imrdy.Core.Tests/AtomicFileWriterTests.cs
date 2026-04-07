using FluentAssertions;

namespace Imrdy.Core.Tests;

public class AtomicFileWriterTests : IDisposable
{
    private readonly string _tempDir;

    public AtomicFileWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imrdy-afw-tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Write_CreatesParentDirectory()
    {
        var path = Path.Combine(_tempDir, "nested", "dir", "file.txt");
        var content = "hello"u8.ToArray();

        AtomicFileWriter.Write(path, content);

        File.Exists(path).Should().BeTrue();
        Directory.Exists(Path.Combine(_tempDir, "nested", "dir")).Should().BeTrue();
    }

    [Fact]
    public void Write_WritesCorrectContent()
    {
        var path = Path.Combine(_tempDir, "file.bin");
        var content = new byte[] { 0x00, 0x42, 0xFF, 0x01 };

        AtomicFileWriter.Write(path, content);

        File.ReadAllBytes(path).Should().Equal(content);
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        var path = Path.Combine(_tempDir, "file.txt");
        AtomicFileWriter.Write(path, "first"u8.ToArray());
        AtomicFileWriter.Write(path, "second"u8.ToArray());

        File.ReadAllText(path).Should().Be("second");
    }

    [Fact]
    public void Write_RemovesTempFile()
    {
        var path = Path.Combine(_tempDir, "file.txt");
        AtomicFileWriter.Write(path, "data"u8.ToArray());

        File.Exists(path + ".tmp").Should().BeFalse();
    }
}

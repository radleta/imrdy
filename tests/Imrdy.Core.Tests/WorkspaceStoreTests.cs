using Imrdy.Core.Workspace;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class WorkspaceStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;
    private readonly WorkspaceStore _store;

    public WorkspaceStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imrdy-ws-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "workspaces.json");
        _store = new WorkspaceStore(_filePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyConfig()
    {
        var config = _store.Load();

        config.Should().NotBeNull();
        config.Workspaces.Should().BeEmpty();
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptyConfig()
    {
        File.WriteAllText(_filePath, "not json");

        var config = _store.Load();

        config.Workspaces.Should().BeEmpty();
    }

    [Fact]
    public void Save_CreatesFile()
    {
        var config = new WorkspaceConfig
        {
            Workspaces = [new WorkspaceEntry { Path = @"D:\dev\test", Name = "test", Desktop = 1 }]
        };

        _store.Save(config);

        File.Exists(_filePath).Should().BeTrue();
    }

    [Fact]
    public void Save_Load_Roundtrip()
    {
        var config = new WorkspaceConfig
        {
            Workspaces =
            [
                new WorkspaceEntry { Path = @"D:\dev\project-a", Name = "project-a", Desktop = 1 },
                new WorkspaceEntry { Path = @"D:\dev\project-b", Name = "project-b", Desktop = 2 },
            ]
        };

        _store.Save(config);
        var loaded = _store.Load();

        loaded.Workspaces.Should().HaveCount(2);
        loaded.Workspaces[0].Path.Should().Be(@"D:\dev\project-a");
        loaded.Workspaces[0].Name.Should().Be("project-a");
        loaded.Workspaces[0].Desktop.Should().Be(1);
        loaded.Workspaces[1].Path.Should().Be(@"D:\dev\project-b");
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        var nestedDir = Path.Combine(_tempDir, "nested", "dir");
        var nestedPath = Path.Combine(nestedDir, "workspaces.json");
        var store = new WorkspaceStore(nestedPath);

        store.Save(new WorkspaceConfig());

        File.Exists(nestedPath).Should().BeTrue();
    }

    [Fact]
    public void Save_AtomicWrite_NoTmpFileRemains()
    {
        _store.Save(new WorkspaceConfig());

        File.Exists(_filePath + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void Pin_NewWorkspace_Adds()
    {
        _store.Pin(@"D:\dev\project", "project", 1);

        var config = _store.Load();
        config.Workspaces.Should().HaveCount(1);
        config.Workspaces[0].Name.Should().Be("project");
    }

    [Fact]
    public void Pin_ExistingPath_Updates()
    {
        _store.Pin(@"D:\dev\project", "old-name", 1);
        _store.Pin(@"D:\dev\project", "new-name", 2);

        var config = _store.Load();
        config.Workspaces.Should().HaveCount(1);
        config.Workspaces[0].Name.Should().Be("new-name");
        config.Workspaces[0].Desktop.Should().Be(2);
    }

    [Fact]
    public void Pin_NormalizesPath()
    {
        _store.Pin(@"D:\dev\project\", "project", 1);

        var config = _store.Load();
        config.Workspaces[0].Path.Should().Be(@"D:\dev\project");
    }

    [Fact]
    public void Unpin_ExistingWorkspace_Removes()
    {
        _store.Pin(@"D:\dev\project", "project", 1);
        _store.Unpin(@"D:\dev\project");

        var config = _store.Load();
        config.Workspaces.Should().BeEmpty();
    }

    [Fact]
    public void Unpin_NonExistent_NoOp()
    {
        _store.Pin(@"D:\dev\project-a", "a", 1);
        _store.Unpin(@"D:\dev\project-b");

        var config = _store.Load();
        config.Workspaces.Should().HaveCount(1);
    }

    [Fact]
    public void SetDesktop_ExistingWorkspace_Updates()
    {
        _store.Pin(@"D:\dev\project", "project", 1);
        _store.SetDesktop(@"D:\dev\project", 3);

        var config = _store.Load();
        config.Workspaces[0].Desktop.Should().Be(3);
    }

    [Fact]
    public void SetDesktop_NonExistent_NoOp()
    {
        _store.Pin(@"D:\dev\project", "project", 1);
        _store.SetDesktop(@"D:\dev\other", 5);

        var config = _store.Load();
        config.Workspaces[0].Desktop.Should().Be(1);
    }

    [Fact]
    public void IsPinned_PinnedPath_ReturnsTrue()
    {
        _store.Pin(@"D:\dev\project", "project", 1);

        _store.IsPinned(@"D:\dev\project").Should().BeTrue();
    }

    [Fact]
    public void IsPinned_UnpinnedPath_ReturnsFalse()
    {
        _store.Pin(@"D:\dev\project-a", "a", 1);

        _store.IsPinned(@"D:\dev\project-b").Should().BeFalse();
    }

    [Fact]
    public void IsPinned_EmptyStore_ReturnsFalse()
    {
        _store.IsPinned(@"D:\dev\project").Should().BeFalse();
    }

    [Fact]
    public void IsPinned_AfterUnpin_ReturnsFalse()
    {
        _store.Pin(@"D:\dev\project", "project", 1);
        _store.Unpin(@"D:\dev\project");

        _store.IsPinned(@"D:\dev\project").Should().BeFalse();
    }
}

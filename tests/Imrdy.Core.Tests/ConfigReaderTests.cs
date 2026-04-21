using System.Text;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class ConfigReaderTests : IDisposable
{
    private readonly string _configPath;

    public ConfigReaderTests()
    {
        // ImrdyPaths.Home is set by the module initializer (TestModuleInit).
        // Ensure the home directory exists and clean up any prior config file.
        Directory.CreateDirectory(ImrdyPaths.Home);
        _configPath = ImrdyPaths.Config;
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }

    public void Dispose()
    {
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }

    [Fact]
    public void Read_MissingFile_ReturnsDefaults()
    {
        var config = ConfigReader.Read();

        config.Should().NotBeNull();
        config.Tray.Enabled.Should().BeTrue();
        config.Sound.Enabled.Should().BeTrue();
        config.Sound.DefaultPack.Should().Be("random");
        config.Sound.Projects.Should().BeEmpty();
    }

    [Fact]
    public void Read_EmptyFile_ReturnsDefaults()
    {
        File.WriteAllText(_configPath, "");

        var config = ConfigReader.Read();

        config.Tray.Enabled.Should().BeTrue();
        config.Sound.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Read_InvalidJson_ReturnsDefaults()
    {
        File.WriteAllText(_configPath, "not json at all");

        var config = ConfigReader.Read();

        config.Tray.Enabled.Should().BeTrue();
        config.Sound.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Read_PartialSchema_FillsDefaults()
    {
        File.WriteAllText(_configPath, """{"tray":{"enabled":false}}""");

        var config = ConfigReader.Read();

        config.Tray.Enabled.Should().BeFalse();
        config.Sound.Enabled.Should().BeTrue();
        config.Sound.DefaultPack.Should().Be("random");
        config.Sound.Projects.Should().BeEmpty();
    }

    [Fact]
    public void Read_ExplicitNullSections_UsesDefaults()
    {
        File.WriteAllText(_configPath, """{"tray":null,"sound":null}""");

        var config = ConfigReader.Read();

        config.Tray.Should().NotBeNull();
        config.Tray.Enabled.Should().BeTrue();
        config.Sound.Should().NotBeNull();
        config.Sound.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Update_ColdStart_CreatesFile()
    {
        File.Exists(_configPath).Should().BeFalse();

        ConfigReader.Update(c => c with { Tray = c.Tray with { Enabled = false } });

        File.Exists(_configPath).Should().BeTrue();
    }

    [Fact]
    public void Update_RoundTrip()
    {
        ConfigReader.Update(c => c with
        {
            Sound = c.Sound with { Enabled = false, DefaultPack = "retro" }
        });

        var config = ConfigReader.Read();

        config.Sound.Enabled.Should().BeFalse();
        config.Sound.DefaultPack.Should().Be("retro");
    }

    [Fact]
    public void Update_PreservesOtherSections()
    {
        // Set up initial state with custom sound config
        ConfigReader.Update(c => c with
        {
            Sound = c.Sound with { DefaultPack = "retro" }
        });

        // Mutate tray only
        ConfigReader.Update(c => c with
        {
            Tray = c.Tray with { Enabled = false }
        });

        var config = ConfigReader.Read();

        config.Tray.Enabled.Should().BeFalse();
        config.Sound.DefaultPack.Should().Be("retro");
    }

    [Fact]
    public void Read_WithMissingIconStyle_DefaultsToDots()
    {
        File.WriteAllText(_configPath, """{"tray":{"enabled":true}}""");

        var config = ConfigReader.Read();

        config.Tray.IconStyle.Should().Be("dots");
    }

    [Fact]
    public void Read_WithEmptyIconStyle_DefaultsToDots()
    {
        File.WriteAllText(_configPath, """{"tray":{"enabled":true,"iconStyle":""}}""");

        var config = ConfigReader.Read();

        config.Tray.IconStyle.Should().Be("dots");
    }

    [Fact]
    public void Read_WithExplicitDotsStyle_Preserves()
    {
        File.WriteAllText(_configPath, """{"tray":{"enabled":true,"iconStyle":"dots"}}""");

        var config = ConfigReader.Read();

        config.Tray.IconStyle.Should().Be("dots");
    }

    [Fact]
    public void Read_WithPackStyle_Preserves()
    {
        File.WriteAllText(_configPath, """{"tray":{"enabled":true,"iconStyle":"pack:test-name"}}""");

        var config = ConfigReader.Read();

        config.Tray.IconStyle.Should().Be("pack:test-name");
    }

    [Fact]
    public void Read_BomFreeOutput()
    {
        ConfigReader.Update(c => c);

        var bytes = File.ReadAllBytes(_configPath);

        // UTF-8 BOM is 0xEF 0xBB 0xBF
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        hasBom.Should().BeFalse("config file should be BOM-free UTF-8");
    }

    [Fact]
    public void Read_DefaultConfig_HasOverlayDefaults()
    {
        var config = ConfigReader.Read();

        config.Overlay.Enabled.Should().BeFalse();
        config.Overlay.Position.Should().Be("bottom-right");
        config.Overlay.Size.Should().Be(64);
        config.Overlay.Spacing.Should().Be(4);
    }

    [Fact]
    public void Read_WithOverlayEnabled_DeserializesCorrectly()
    {
        File.WriteAllText(_configPath, """{"overlay":{"enabled":true,"position":"bottom-left","size":128,"spacing":8}}""");

        var config = ConfigReader.Read();

        config.Overlay.Enabled.Should().BeTrue();
        config.Overlay.Position.Should().Be("bottom-left");
        config.Overlay.Size.Should().Be(128);
        config.Overlay.Spacing.Should().Be(8);
    }

    [Fact]
    public void EnsureDefaults_WithNullOverlay_SetsDefaults()
    {
        File.WriteAllText(_configPath, """{"overlay":null}""");

        var config = ConfigReader.Read();

        config.Overlay.Should().NotBeNull();
        config.Overlay.Enabled.Should().BeFalse();
        config.Overlay.Position.Should().Be("bottom-right");
        config.Overlay.Size.Should().Be(64);
        config.Overlay.Spacing.Should().Be(4);
    }

    [Fact]
    public void EnsureDefaults_WithEmptyPosition_DefaultsToBottomRight()
    {
        File.WriteAllText(_configPath, """{"overlay":{"enabled":true,"position":"","size":64,"spacing":4}}""");

        var config = ConfigReader.Read();

        config.Overlay.Position.Should().Be("bottom-right");
    }

    [Fact]
    public void Read_ConfigMissingInteractiveField_DefaultsToTrue()
    {
        File.WriteAllText(_configPath, """{"overlay":{"enabled":true,"position":"bottom-left","size":48,"spacing":2}}""");

        var config = ConfigReader.Read();

        config.Overlay.Interactive.Should().BeTrue();
        config.Overlay.Position.Should().Be("bottom-left");
        config.Overlay.Size.Should().Be(48);
        config.Overlay.Spacing.Should().Be(2);
    }

    [Fact]
    public void Read_ConfigInteractiveFalse_Preserved()
    {
        File.WriteAllText(_configPath, """{"overlay":{"interactive":false}}""");

        var config = ConfigReader.Read();

        config.Overlay.Interactive.Should().BeFalse();
    }

    [Fact]
    public void Read_MissingOverlay_InteractiveDefaultsToTrue()
    {
        File.WriteAllText(_configPath, "{}");

        var config = ConfigReader.Read();

        config.Overlay.Interactive.Should().BeTrue();
    }
}

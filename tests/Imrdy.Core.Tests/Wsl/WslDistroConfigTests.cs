using System.Text.Json;
using FluentAssertions;
using Imrdy.Core.Wsl;

namespace Imrdy.Core.Tests.Wsl;

public class WslDistroConfigTests
{
    [Fact]
    public void Defaults_WatchAll_IsTrue()
    {
        var config = new WslDistroConfig();

        config.WatchAll.Should().BeTrue();
    }

    [Fact]
    public void Defaults_Distros_IsNull()
    {
        var config = new WslDistroConfig();

        config.Distros.Should().BeNull();
    }

    [Fact]
    public void NullSafeIteration_NullDistros_DoesNotThrow()
    {
        var config = new WslDistroConfig();

        var act = () => (config.Distros ?? []).Count;

        act.Should().NotThrow();
        (config.Distros ?? []).Count.Should().Be(0);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new WslDistroConfig
        {
            WatchAll = false,
            Distros =
            [
                new WslDistroEntry
                {
                    Name = "Ubuntu-22.04",
                    LinuxHomes = ["/home/radle"],
                    Enabled = true,
                    DiscoveredAt = new DateTimeOffset(2026, 4, 28, 0, 0, 0, TimeSpan.Zero),
                },
            ],
        };

        var json = JsonSerializer.Serialize(original, ImrdyJsonContext.Default.WslDistroConfig);
        var deserialized = JsonSerializer.Deserialize(json, ImrdyJsonContext.Default.WslDistroConfig);

        deserialized.Should().NotBeNull();
        deserialized!.WatchAll.Should().BeFalse();
        deserialized.Distros.Should().HaveCount(1);
        deserialized.Distros![0].Name.Should().Be("Ubuntu-22.04");
        deserialized.Distros[0].LinuxHomes.Should().ContainSingle().Which.Should().Be("/home/radle");
        deserialized.Distros[0].Enabled.Should().BeTrue();
        deserialized.Distros[0].DiscoveredAt.Should().Be(new DateTimeOffset(2026, 4, 28, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void JsonShape_MatchesSpec()
    {
        var json = """
            {
              "watch_all": true,
              "distros": [
                {
                  "name": "Ubuntu-22.04",
                  "linux_homes": ["/home/radle"],
                  "enabled": true,
                  "discovered_at": "2026-04-28T00:00:00+00:00"
                },
                {
                  "name": "Ubuntu-24.04",
                  "linux_homes": ["/home/radle"],
                  "enabled": true,
                  "discovered_at": "2026-04-28T00:00:00+00:00"
                }
              ]
            }
            """;

        var config = JsonSerializer.Deserialize(json, ImrdyJsonContext.Default.WslDistroConfig);

        config.Should().NotBeNull();
        config!.WatchAll.Should().BeTrue();
        config.Distros.Should().HaveCount(2);
        config.Distros![0].Name.Should().Be("Ubuntu-22.04");
        config.Distros[0].LinuxHomes.Should().ContainSingle().Which.Should().Be("/home/radle");
        config.Distros[0].Enabled.Should().BeTrue();
        config.Distros[1].Name.Should().Be("Ubuntu-24.04");
    }

    [Fact]
    public void Deserialization_NullDistros_LeavesDistrosNull()
    {
        var json = """{"watch_all": true}""";

        var config = JsonSerializer.Deserialize(json, ImrdyJsonContext.Default.WslDistroConfig);

        config.Should().NotBeNull();
        config!.Distros.Should().BeNull();
    }

    [Fact]
    public void WslDistroEntry_Defaults_EnabledTrue()
    {
        var entry = new WslDistroEntry { Name = "Ubuntu-22.04" };

        entry.Enabled.Should().BeTrue();
        entry.LinuxHomes.Should().BeNull();
        entry.DiscoveredAt.Should().Be(default);
    }
}

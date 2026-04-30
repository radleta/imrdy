using FluentAssertions;
using Imrdy.Core;
using Imrdy.Core.State;
using Imrdy.Core.Wsl;
using Imrdy.Windows.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Xunit;

namespace Imrdy.Windows.Tests.Commands;

/// <summary>
/// Unit tests for WslCommand verifying argument parsing, output formatting, exit codes,
/// and store mutation without spawning a real binary or touching real WSL paths.
/// </summary>
public class WslCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _wslDistrosPath;
    private readonly WslDistroStore _store;

    public WslCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imrdy-wsl-cmd-tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _wslDistrosPath = Path.Combine(_tempDir, "wsl-distros.json");
        _store = new WslDistroStore(_wslDistrosPath);

        // Prevent wsl.exe subprocess calls during unit tests. All List tests
        // treat every configured distro as stopped (empty running set), which is
        // sufficient for structure and exit-code assertions.
        WslDistroDiscovery.RunningDistrosOverride = static () => Array.Empty<string>();
    }

    public void Dispose()
    {
        WslDistroDiscovery.RunningDistrosOverride = null;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (ServiceProvider Services, StringWriter Output) BuildServices()
    {
        var sw = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(sw),
        });

        var sc = new ServiceCollection();
        sc.AddSingleton(_store);
        sc.AddSingleton<StateFileReader>();
        sc.AddSingleton<IAnsiConsole>(console);
        sc.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        return (sc.BuildServiceProvider(), sw);
    }

    private static int Run(ServiceProvider sp, params string[] args)
    {
        var json = args.Any(a => a == "--json");
        var cleaned = args.Where(a => a != "--json").ToArray();
        return WslCommand.Run(sp, cleaned, json);
    }

    // ── list ──────────────────────────────────────────────────────────────────

    [Fact]
    public void List_EmptyStore_ReturnsZeroAndEmptyMessage()
    {
        var (sp, sw) = BuildServices();
        using (sp)
        {
            var code = Run(sp, "list");

            code.Should().Be(0);
            sw.ToString().Should().Contain("No WSL distros");
        }
    }

    [Fact]
    public void List_WithDistros_ReturnsZeroAndTable()
    {
        _store.Add("Ubuntu-22.04", "/home/alice");
        var (sp, sw) = BuildServices();
        using (sp)
        {
            var code = Run(sp, "list");

            code.Should().Be(0);
            sw.ToString().Should().Contain("Ubuntu-22.04");
        }
    }

    [Fact]
    public void List_DefaultArgs_SameAsExplicitList()
    {
        _store.Add("Debian", null);
        var (sp1, sw1) = BuildServices();
        var (sp2, sw2) = BuildServices();
        using (sp1)
        using (sp2)
        {
            var codeDefault = WslCommand.Run(sp1, [], json: false);
            var codeExplicit = Run(sp2, "list");

            codeDefault.Should().Be(0);
            codeExplicit.Should().Be(0);
        }
    }

    [Fact]
    public void List_JsonFlag_ProducesParseableJsonArray()
    {
        _store.Add("Ubuntu-22.04", "/home/alice");
        _store.Add("Debian", null);

        // JSON is now written through IAnsiConsole, so StringWriter captures it directly.
        var (sp, sw) = BuildServices();
        using (sp)
        {
            var code = WslCommand.Run(sp, [], json: true);
            code.Should().Be(0);

            var json = System.Text.Json.JsonDocument.Parse(sw.ToString());
            json.RootElement.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);
            json.RootElement.GetArrayLength().Should().Be(2);

            // Verify schema: name, status, sessions, enabled
            var first = json.RootElement.EnumerateArray().First();
            first.TryGetProperty("name", out _).Should().BeTrue();
            first.TryGetProperty("status", out _).Should().BeTrue();
            first.TryGetProperty("sessions", out _).Should().BeTrue();
            first.TryGetProperty("enabled", out _).Should().BeTrue();
        }
    }

    // ── add ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_ValidDistro_ReturnsZeroAndPersists()
    {
        var (sp, sw) = BuildServices();
        using (sp)
        {
            var code = Run(sp, "add", "Ubuntu-22.04");

            code.Should().Be(0);
            sw.ToString().Should().Contain("Ubuntu-22.04");

            var config = _store.Load();
            config.Distros.Should().ContainSingle(d => d.Name == "Ubuntu-22.04");
        }
    }

    [Fact]
    public void Add_WithLinuxHome_StoresHome()
    {
        var (sp, _) = BuildServices();
        using (sp)
        {
            var code = Run(sp, "add", "Ubuntu-24.04", "--linux-home", "/home/alice");

            code.Should().Be(0);

            var config = _store.Load();
            var entry = config.Distros!.Single(d => d.Name == "Ubuntu-24.04");
            entry.LinuxHomes.Should().Contain("/home/alice");
        }
    }

    [Fact]
    public void Add_MissingDistroArg_ReturnsOne()
    {
        var (sp, sw) = BuildServices();
        using (sp)
        {
            var code = Run(sp, "add");

            code.Should().Be(1);
            sw.ToString().Should().ContainAny("Usage", "usage");
        }
    }

    [Fact]
    public void Add_DuplicateEntry_ReturnsZeroAndIsIdempotent()
    {
        _store.Add("Ubuntu-22.04", null);

        var (sp, _) = BuildServices();
        using (sp)
        {
            var code = Run(sp, "add", "Ubuntu-22.04");

            code.Should().Be(0);
            // Still only one entry
            _store.Load().Distros!.Count(d => d.Name == "Ubuntu-22.04").Should().Be(1);
        }
    }

    [Fact]
    public void Add_JsonFlag_ProducesParseableJsonObject()
    {
        var (sp, sw) = BuildServices();
        using (sp)
        {
            var code = WslCommand.Run(sp, ["add", "Ubuntu-22.04"], json: true);

            code.Should().Be(0);

            var json = System.Text.Json.JsonDocument.Parse(sw.ToString());
            json.RootElement.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
            json.RootElement.TryGetProperty("name", out var nameProp).Should().BeTrue();
            nameProp.GetString().Should().Be("Ubuntu-22.04");
            json.RootElement.TryGetProperty("added", out var addedProp).Should().BeTrue();
            addedProp.GetBoolean().Should().BeTrue();
        }
    }

    [Fact]
    public void Add_NameWithBackslash_ReturnsOneAndRejects()
    {
        var (sp, sw) = BuildServices();
        using (sp)
        {
            var code = Run(sp, "add", @"foo\bar");

            code.Should().Be(1);
            sw.ToString().Should().Contain("path separators");
            _store.Load().Distros.Should().BeNullOrEmpty();
        }
    }

    [Fact]
    public void Add_NameWithForwardSlash_ReturnsOneAndRejects()
    {
        var (sp, sw) = BuildServices();
        using (sp)
        {
            var code = Run(sp, "add", "foo/bar");

            code.Should().Be(1);
            sw.ToString().Should().Contain("path separators");
        }
    }

    [Fact]
    public void Add_LinuxHomeWithDotDot_ReturnsOneAndRejects()
    {
        var (sp, sw) = BuildServices();
        using (sp)
        {
            var code = Run(sp, "add", "Ubuntu-22.04", "--linux-home", "/home/alice/../../Windows");

            code.Should().Be(1);
            sw.ToString().Should().Contain("..");
            _store.Load().Distros.Should().BeNullOrEmpty();
        }
    }

    // ── remove ────────────────────────────────────────────────────────────────

    [Fact]
    public void Remove_ExistingEntry_ReturnsZeroAndDeletes()
    {
        _store.Add("Debian", null);

        var (sp, sw) = BuildServices();
        using (sp)
        {
            var code = Run(sp, "remove", "Debian");

            code.Should().Be(0);
            sw.ToString().Should().Contain("Debian");
            _store.Load().Distros.Should().NotContain(d => d.Name == "Debian");
        }
    }

    [Fact]
    public void Remove_NotFoundEntry_ReturnsZero()
    {
        var (sp, _) = BuildServices();
        using (sp)
        {
            // Per spec: not-found is success (exit 0)
            var code = Run(sp, "remove", "NonExistent");

            code.Should().Be(0);
        }
    }

    [Fact]
    public void Remove_MissingDistroArg_ReturnsOne()
    {
        var (sp, sw) = BuildServices();
        using (sp)
        {
            var code = Run(sp, "remove");

            code.Should().Be(1);
            sw.ToString().Should().ContainAny("Usage", "usage");
        }
    }

    [Fact]
    public void Remove_JsonFlag_ProducesParseableJsonObject()
    {
        _store.Add("Debian", null);

        var (sp, sw) = BuildServices();
        using (sp)
        {
            var code = WslCommand.Run(sp, ["remove", "Debian"], json: true);

            code.Should().Be(0);

            var json = System.Text.Json.JsonDocument.Parse(sw.ToString());
            json.RootElement.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
            json.RootElement.TryGetProperty("name", out var nameProp).Should().BeTrue();
            nameProp.GetString().Should().Be("Debian");
            json.RootElement.TryGetProperty("removed", out var removedProp).Should().BeTrue();
            removedProp.GetBoolean().Should().BeTrue();
        }
    }

    // ── help ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public void Help_VariousForms_ReturnsZeroAndPrintsUsage(string helpArg)
    {
        var (sp, sw) = BuildServices();
        using (sp)
        {
            var code = Run(sp, helpArg);

            code.Should().Be(0);
            sw.ToString().Should().Contain("Usage:");
        }
    }

    [Fact]
    public void Help_ContainsSubcommandList()
    {
        var (sp, sw) = BuildServices();
        using (sp)
        {
            Run(sp, "--help");

            var output = sw.ToString();
            output.Should().Contain("list");
            output.Should().Contain("add");
            output.Should().Contain("remove");
        }
    }

    // ── unknown subcommand ────────────────────────────────────────────────────

    [Fact]
    public void UnknownSubcommand_ReturnsOneAndPrintsError()
    {
        var (sp, sw) = BuildServices();
        using (sp)
        {
            var code = Run(sp, "bogus");

            code.Should().Be(1);
            sw.ToString().Should().Contain("bogus");
        }
    }

    // ── exit codes ────────────────────────────────────────────────────────────

    [Fact]
    public void ExitCodes_ListSucceeds_Zero()
    {
        var (sp, _) = BuildServices();
        using (sp)
        {
            Run(sp, "list").Should().Be(0);
        }
    }

    [Fact]
    public void ExitCodes_AddMissingArg_One()
    {
        var (sp, _) = BuildServices();
        using (sp)
        {
            Run(sp, "add").Should().Be(1);
        }
    }

    [Fact]
    public void ExitCodes_RemoveMissingArg_One()
    {
        var (sp, _) = BuildServices();
        using (sp)
        {
            Run(sp, "remove").Should().Be(1);
        }
    }

    [Fact]
    public void ExitCodes_UnknownSubcommand_One()
    {
        var (sp, _) = BuildServices();
        using (sp)
        {
            Run(sp, "xyz").Should().Be(1);
        }
    }
}

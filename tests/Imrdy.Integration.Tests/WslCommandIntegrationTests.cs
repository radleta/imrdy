using System.Text.Json;
using FluentAssertions;
using Imrdy.Integration.Tests.Helpers;
using Xunit;

namespace Imrdy.Integration.Tests;

/// <summary>
/// Integration tests for <c>imrdy wsl</c> subcommands.
/// Invokes the published binary and asserts exit codes, stdout, and file mutations.
/// </summary>
[Trait("Category", "Integration")]
public class WslCommandIntegrationTests : IDisposable
{
    private readonly CliTestFixture _cli = new();
    private readonly TempDirectoryFixture _temp = new();

    private Dictionary<string, string> EnvWithHome() => new()
    {
        ["IMRDY_HOME"] = _temp.Path,
    };

    public void Dispose() => _temp.Dispose();

    // ── add ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_ValidDistro_ExitsZeroAndMutatesFile()
    {
        var (exitCode, stdout, _) = await _cli.RunAsync(
            "wsl add Ubuntu-22.04 --linux-home /home/foo",
            environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
        stdout.Should().Contain("Ubuntu-22.04");

        var wslFile = Path.Combine(_temp.Path, "wsl-distros.json");
        File.Exists(wslFile).Should().BeTrue("wsl add must create wsl-distros.json");
        var content = File.ReadAllText(wslFile);
        content.Should().Contain("Ubuntu-22.04");
        content.Should().Contain("/home/foo");
    }

    [Fact]
    public async Task Add_MissingDistroArg_ExitsOne()
    {
        var (exitCode, _, _) = await _cli.RunAsync(
            "wsl add",
            environmentVariables: EnvWithHome());

        exitCode.Should().Be(1);
    }

    // ── list ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_EmptyStore_ExitsZero()
    {
        var (exitCode, _, _) = await _cli.RunAsync(
            "wsl list",
            environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task List_Json_ExitsZeroAndReturnsParseableArray()
    {
        // Seed a distro first
        await _cli.RunAsync("wsl add Ubuntu-22.04", environmentVariables: EnvWithHome());

        var (exitCode, stdout, _) = await _cli.RunAsync(
            "wsl list --json",
            environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);

        var doc = JsonDocument.Parse(stdout);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(1);

        var entry = doc.RootElement[0];
        entry.GetProperty("name").GetString().Should().Be("Ubuntu-22.04");
        entry.GetProperty("status").GetString().Should().BeOneOf("running", "stopped");
        entry.GetProperty("sessions").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        entry.GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task List_AfterAdd_ContainsAddedDistro()
    {
        await _cli.RunAsync("wsl add Debian --linux-home /home/user", environmentVariables: EnvWithHome());

        var (exitCode, stdout, _) = await _cli.RunAsync(
            "wsl list --json",
            environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
        stdout.Should().Contain("Debian");
    }

    // ── remove ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_ExistingDistro_ExitsZeroAndRemovesEntry()
    {
        await _cli.RunAsync("wsl add Ubuntu-22.04", environmentVariables: EnvWithHome());

        var (exitCode, _, _) = await _cli.RunAsync(
            "wsl remove Ubuntu-22.04",
            environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);

        var (_, stdout, _) = await _cli.RunAsync(
            "wsl list --json",
            environmentVariables: EnvWithHome());

        var doc = JsonDocument.Parse(stdout);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Remove_NotFound_ExitsZero()
    {
        // Per spec: removing a non-existent distro is success
        var (exitCode, _, _) = await _cli.RunAsync(
            "wsl remove NonExistent",
            environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Remove_SecondCall_AlsoExitsZero()
    {
        await _cli.RunAsync("wsl add Ubuntu-22.04", environmentVariables: EnvWithHome());
        await _cli.RunAsync("wsl remove Ubuntu-22.04", environmentVariables: EnvWithHome());

        var (exitCode, _, _) = await _cli.RunAsync(
            "wsl remove Ubuntu-22.04",
            environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Remove_MissingArg_ExitsOne()
    {
        var (exitCode, _, _) = await _cli.RunAsync(
            "wsl remove",
            environmentVariables: EnvWithHome());

        exitCode.Should().Be(1);
    }

    // ── piping ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_WithStdinInput_IgnoresStdinAndExitsZero()
    {
        var (exitCode, _, _) = await _cli.RunAsync(
            "wsl list",
            stdin: "",
            environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
    }

    // ── help ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Help_ExitsZeroAndContainsSubcommands()
    {
        var (exitCode, stdout, _) = await _cli.RunAsync(
            "wsl --help",
            environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
        stdout.Should().ContainAll("list", "add", "remove");
    }
}

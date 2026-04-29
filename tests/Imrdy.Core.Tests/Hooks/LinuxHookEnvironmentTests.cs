using FluentAssertions;
using Imrdy.Core.Hooks;
using Imrdy.Core.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Imrdy.Core.Tests.Hooks;

/// <summary>
/// Verifies HookCommand.Run behavior using a Linux-style IHookEnvironment stub:
/// null PID, no-op tray, verbatim cwd, no-op OnSessionEnd.
/// These tests validate the contract path that Imrdy.Linux will consume.
/// </summary>
public class LinuxHookEnvironmentTests : IDisposable
{
    private readonly string _sessionsDir;
    private readonly ServiceProvider _services;

    public LinuxHookEnvironmentTests()
    {
        _sessionsDir = ImrdyPaths.Sessions;
        Directory.CreateDirectory(_sessionsDir);

        var sc = new ServiceCollection();
        sc.AddSingleton<StateFileReader>();
        sc.AddSingleton(NullLoggerFactory.Instance.CreateLogger("test"));
        sc.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        _services = sc.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    // ── Linux-style stub ──────────────────────────────────────────────────────

    private sealed class LinuxStyleEnv : IHookEnvironment
    {
        public int? ResolveTerminalPid(int currentPid, string sessionId) => null;
        public void EnsureTrayRunning() { }
        public string NormalizeCwd(string? cwd) => cwd ?? "";
        public void OnSessionEnd(string sessionId) { }
        public string? GetWslDistro() => Environment.GetEnvironmentVariable("WSL_DISTRO_NAME");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildJson(
        string hookEventName,
        string sessionId,
        string cwd = "/home/foo/project",
        string? wslDistro = null)
    {
        var parts = new List<string>
        {
            $"\"hook_event_name\":\"{hookEventName}\"",
            $"\"session_id\":\"{sessionId}\"",
            $"\"cwd\":\"{cwd}\"",
        };
        if (wslDistro is not null)
            parts.Add($"\"wsl_distro\":\"{wslDistro}\"");
        return "{" + string.Join(",", parts) + "}";
    }

    private string StateFilePath(string sessionId)
        => Path.Combine(_sessionsDir, $"{sessionId}.json");

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Run_LinuxPayload_WritesStateFileSuccessfully()
    {
        const string sid = "linux-env-test-01";
        var json = BuildJson("SessionStart", sid, cwd: "/home/foo/project");
        var env = new LinuxStyleEnv();

        var result = HookCommand.Run(_services, new StringReader(json), env);

        result.Should().Be(0);
        File.Exists(StateFilePath(sid)).Should().BeTrue("state file must be written on Linux SessionStart");
    }

    [Fact]
    public void Run_LinuxPayload_PreservesLinuxCwdVerbatim()
    {
        const string sid = "linux-env-test-02";
        const string linuxCwd = "/home/foo/my-project";
        var json = BuildJson("UserPromptSubmit", sid, cwd: linuxCwd);
        var env = new LinuxStyleEnv();

        HookCommand.Run(_services, new StringReader(json), env);

        var reader = _services.GetRequiredService<StateFileReader>();
        var state = reader.ReadStateFile(StateFilePath(sid));
        state.Should().NotBeNull();
        state!.Cwd.Should().Be(linuxCwd, "Linux cwd must not be mangled by path normalization");
    }

    [Fact]
    public void Run_LinuxPayload_WslDistroPreservedInStateFile()
    {
        const string sid = "linux-env-test-03";
        const string distro = "Ubuntu-22.04";
        var json = BuildJson("SessionStart", sid, wslDistro: distro);
        var env = new LinuxStyleEnv();

        var result = HookCommand.Run(_services, new StringReader(json), env);

        result.Should().Be(0);
        var reader = _services.GetRequiredService<StateFileReader>();
        var state = reader.ReadStateFile(StateFilePath(sid));
        state.Should().NotBeNull();
        state!.WslDistro.Should().Be(distro, "WslDistro must be threaded from hook JSON through to state file");
    }

    [Fact]
    public void Run_LinuxPayload_EmptyStdin_ReturnsZero()
    {
        var env = new LinuxStyleEnv();

        var result = HookCommand.Run(_services, new StringReader(""), env);

        result.Should().Be(0, "empty stdin is valid on Linux (e.g. piped empty input)");
    }

    [Fact]
    public void Run_LinuxPayload_NullPid_DoesNotBlockWrite()
    {
        const string sid = "linux-env-test-05";
        var json = BuildJson("PreToolUse", sid, cwd: "/home/user/repo");
        var env = new LinuxStyleEnv();

        var result = HookCommand.Run(_services, new StringReader(json), env);

        result.Should().Be(0);
        var reader = _services.GetRequiredService<StateFileReader>();
        var state = reader.ReadStateFile(StateFilePath(sid));
        state.Should().NotBeNull();
        state!.ClaudePid.Should().BeNull("Linux hook env returns null PID — state file must still be written");
    }

    [Fact]
    public void GetWslDistro_WhenEnvVarSet_ReturnsValue()
    {
        const string distro = "Ubuntu-22.04-Test";
        Environment.SetEnvironmentVariable("WSL_DISTRO_NAME", distro);
        try
        {
            var env = new LinuxStyleEnv();
            env.GetWslDistro().Should().Be(distro);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WSL_DISTRO_NAME", null);
        }
    }

    [Fact]
    public void GetWslDistro_WhenEnvVarUnset_ReturnsNull()
    {
        var prior = Environment.GetEnvironmentVariable("WSL_DISTRO_NAME");
        Environment.SetEnvironmentVariable("WSL_DISTRO_NAME", null);
        try
        {
            var env = new LinuxStyleEnv();
            env.GetWslDistro().Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("WSL_DISTRO_NAME", prior);
        }
    }

    [Fact]
    public void Run_WslDistroFromEnvVar_PopulatesStateFileWhenJsonFieldAbsent()
    {
        const string sid = "linux-env-test-06";
        const string distro = "Ubuntu-22.04-EnvTest";
        Environment.SetEnvironmentVariable("WSL_DISTRO_NAME", distro);
        try
        {
            // JSON has NO wsl_distro field — env var must supply it
            var json = BuildJson("SessionStart", sid, cwd: "/home/foo/project");
            var env = new LinuxStyleEnv();

            var result = HookCommand.Run(_services, new StringReader(json), env);

            result.Should().Be(0);
            var reader = _services.GetRequiredService<StateFileReader>();
            var state = reader.ReadStateFile(StateFilePath(sid));
            state.Should().NotBeNull();
            state!.WslDistro.Should().Be(distro,
                "WslDistro must be populated from WSL_DISTRO_NAME when not present in hook JSON");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WSL_DISTRO_NAME", null);
        }
    }
}

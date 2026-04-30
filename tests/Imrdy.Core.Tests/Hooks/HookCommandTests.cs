using FluentAssertions;
using Imrdy.Core.Hooks;
using Imrdy.Core.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Imrdy.Core.Tests.Hooks;

/// <summary>
/// Unit tests for HookCommand.Run verifying IHookEnvironment dispatch paths and WslDistro threading.
/// Uses a temp sessions directory (IMRDY_HOME set by TestModuleInit) to avoid touching real state files.
/// </summary>
public class HookCommandTests : IDisposable
{
    private readonly string _sessionsDir;
    private readonly ServiceProvider _services;

    public HookCommandTests()
    {
        // IMRDY_HOME is set to a temp dir by TestModuleInit — sessions go there
        _sessionsDir = ImrdyPaths.Sessions;
        Directory.CreateDirectory(_sessionsDir);

        var sc = new ServiceCollection();
        sc.AddSingleton<StateFileReader>();
        sc.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        _services = sc.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string BuildJson(
        string hookEventName,
        string sessionId,
        string cwd = "/home/user/project",
        string? agentId = null,
        string? wslDistro = null)
    {
        var parts = new List<string>
        {
            $"\"hook_event_name\":\"{hookEventName}\"",
            $"\"session_id\":\"{sessionId}\"",
            $"\"cwd\":\"{cwd}\"",
        };
        if (agentId is not null)
            parts.Add($"\"agent_id\":\"{agentId}\"");
        if (wslDistro is not null)
            parts.Add($"\"wsl_distro\":\"{wslDistro}\"");
        return "{" + string.Join(",", parts) + "}";
    }

    private string StateFilePath(string sessionId)
        => Path.Combine(_sessionsDir, $"{sessionId}.json");

    // ── Stub ─────────────────────────────────────────────────────────────────

    private sealed class RecordingEnv : IHookEnvironment
    {
        public List<string> NormalizeCwdCalls { get; } = [];
        public List<string> OnSessionEndCalls { get; } = [];
        public int EnsureTrayRunningCallCount { get; private set; }
        public int ResolveTerminalPidCallCount { get; private set; }
        public string? WslDistroOverride { get; init; }

        public string NormalizeCwd(string? cwd)
        {
            var result = cwd ?? "";
            NormalizeCwdCalls.Add(result);
            return result;
        }

        public void EnsureTrayRunning() => EnsureTrayRunningCallCount++;

        public int? ResolveTerminalPid(int currentPid, string sessionId)
        {
            ResolveTerminalPidCallCount++;
            return null;
        }

        public void OnSessionEnd(string sessionId) => OnSessionEndCalls.Add(sessionId);

        public string? GetWslDistro() => WslDistroOverride;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Run_SessionStart_WritesStateFileWithWslDistro()
    {
        const string sid = "test-wsl-distro-01";
        const string distro = "Ubuntu-22.04";
        var json = BuildJson("SessionStart", sid, wslDistro: distro);
        var env = new RecordingEnv();

        var result = HookCommand.Run(_services, new StringReader(json), env);

        result.Should().Be(0);
        var stateFile = StateFilePath(sid);
        File.Exists(stateFile).Should().BeTrue("state file must be written on SessionStart");

        var reader = _services.GetRequiredService<StateFileReader>();
        var state = reader.ReadStateFile(stateFile);
        state.Should().NotBeNull();
        state!.WslDistro.Should().Be(distro);
    }

    [Fact]
    public void Run_SessionEnd_CallsOnSessionEnd()
    {
        const string sid = "test-session-end-01";
        var json = BuildJson("SessionEnd", sid);
        var env = new RecordingEnv();

        var result = HookCommand.Run(_services, new StringReader(json), env);

        result.Should().Be(0);
        env.OnSessionEndCalls.Should().ContainSingle(s => s == sid,
            "OnSessionEnd must be called exactly once with the session id");
    }

    [Fact]
    public void Run_TeammateEvent_CallsEnsureTrayRunning()
    {
        // First write a lead state so the teammate branch finds an existing entry
        const string sid = "test-teammate-01";
        var leadJson = BuildJson("UserPromptSubmit", sid);
        var leadEnv = new RecordingEnv();
        HookCommand.Run(_services, new StringReader(leadJson), leadEnv);

        // Now fire a teammate event
        var teammateJson = BuildJson("PostToolUse", sid, agentId: "agent-abc");
        var env = new RecordingEnv();

        var result = HookCommand.Run(_services, new StringReader(teammateJson), env);

        result.Should().Be(0);
        env.EnsureTrayRunningCallCount.Should().Be(1,
            "EnsureTrayRunning must be called on the teammate path");
        env.NormalizeCwdCalls.Should().BeEmpty(
            "NormalizeCwd must NOT be called on the teammate path");
    }

    [Fact]
    public void Run_LeadEvent_CallsNormalizeCwdAndResolveTerminalPidAndEnsureTrayRunning()
    {
        const string sid = "test-lead-01";
        var json = BuildJson("UserPromptSubmit", sid, cwd: "/home/user/myproject");
        var env = new RecordingEnv();

        var result = HookCommand.Run(_services, new StringReader(json), env);

        result.Should().Be(0);
        env.NormalizeCwdCalls.Should().NotBeEmpty(
            "NormalizeCwd must be called at least once on the lead path");
        env.ResolveTerminalPidCallCount.Should().Be(1,
            "ResolveTerminalPid must be called once on the lead path");
        env.EnsureTrayRunningCallCount.Should().Be(1,
            "EnsureTrayRunning must be called on the lead path");
    }

    [Fact]
    public void Run_WslDistro_EnvFallback_WhenJsonFieldAbsent()
    {
        const string sid = "test-wsl-fallback-01";
        const string envDistro = "Ubuntu-22.04-EnvFallback";
        // JSON has NO wsl_distro field
        var json = BuildJson("SessionStart", sid);
        var env = new RecordingEnv { WslDistroOverride = envDistro };

        HookCommand.Run(_services, new StringReader(json), env);

        var reader = _services.GetRequiredService<StateFileReader>();
        var state = reader.ReadStateFile(StateFilePath(sid));
        state!.WslDistro.Should().Be(envDistro,
            "env-var value must fill WslDistro when JSON field is absent");
    }

    [Fact]
    public void Run_WslDistro_JsonFieldWins_WhenBothPresent()
    {
        const string sid = "test-wsl-precedence-01";
        const string jsonDistro = "Ubuntu-JSON";
        const string envDistro = "Ubuntu-ENV";
        // JSON explicitly supplies wsl_distro
        var json = BuildJson("SessionStart", sid, wslDistro: jsonDistro);
        var env = new RecordingEnv { WslDistroOverride = envDistro };

        HookCommand.Run(_services, new StringReader(json), env);

        var reader = _services.GetRequiredService<StateFileReader>();
        var state = reader.ReadStateFile(StateFilePath(sid));
        state!.WslDistro.Should().Be(jsonDistro,
            "JSON wsl_distro must win over the env-var fallback when both are present");
    }

    [Fact]
    public void Run_WslDistro_NullWhenBothAbsent()
    {
        const string sid = "test-wsl-null-01";
        // Neither JSON field nor env override
        var json = BuildJson("SessionStart", sid);
        var env = new RecordingEnv { WslDistroOverride = null };

        HookCommand.Run(_services, new StringReader(json), env);

        var reader = _services.GetRequiredService<StateFileReader>();
        var state = reader.ReadStateFile(StateFilePath(sid));
        state!.WslDistro.Should().BeNull(
            "WslDistro must be null when neither JSON field nor env-var is present");
    }

    [Fact]
    public void Run_TeammateBeforeLead_WritesMinimalStateFileWithLastTeammateAt()
    {
        // No lead state file exists — teammate event arrives first
        const string sid = "test-teammate-before-lead-01";
        var json = BuildJson("PostToolUse", sid, agentId: "agent-xyz");
        var env = new RecordingEnv();

        var result = HookCommand.Run(_services, new StringReader(json), env);

        result.Should().Be(0);
        var stateFile = StateFilePath(sid);
        File.Exists(stateFile).Should().BeTrue("state file must be synthesized when teammate fires before lead");

        var reader = _services.GetRequiredService<StateFileReader>();
        var state = reader.ReadStateFile(stateFile);
        state.Should().NotBeNull();
        state!.LastTeammateAt.Should().NotBeNull("LastTeammateAt must be set in the synthesized state");
        state.SessionId.Should().Be(sid);

        var expectedStatus = Imrdy.Core.Status.StatusDerivation.DeriveStatus("PostToolUse");
        state.Status.Should().Be(expectedStatus, "Status must match DeriveStatus for the hook event");
    }

    [Fact]
    public void Run_LeadAfterTeammate_PreservesLastTeammateAtViaFieldPreservation()
    {
        // Step 1: fire teammate event with no existing lead — synthesizes state with LastTeammateAt
        const string sid = "test-teammate-before-lead-02";
        var teammateJson = BuildJson("PostToolUse", sid, agentId: "agent-xyz");
        var env = new RecordingEnv();
        HookCommand.Run(_services, new StringReader(teammateJson), env);

        var reader = _services.GetRequiredService<StateFileReader>();
        var afterTeammate = reader.ReadStateFile(StateFilePath(sid));
        afterTeammate!.LastTeammateAt.Should().NotBeNull("precondition: teammate state must have LastTeammateAt");
        var capturedAt = afterTeammate.LastTeammateAt!.Value;

        // Step 2: fire lead SessionStart — FieldPreservation must carry LastTeammateAt forward
        var leadJson = BuildJson("SessionStart", sid, cwd: "/home/user/myproject");
        HookCommand.Run(_services, new StringReader(leadJson), env);

        var afterLead = reader.ReadStateFile(StateFilePath(sid));
        afterLead.Should().NotBeNull();
        afterLead!.LastTeammateAt.Should().NotBeNull("LastTeammateAt must survive the lead SessionStart merge");
        afterLead.LastTeammateAt!.Value.Should().Be(capturedAt,
            "FieldPreservation must carry the original LastTeammateAt from the synthesized state");
    }

    [Fact]
    public void Run_TeammateBeforeLead_ProjectMirrorsLeadPath()
    {
        // Verify that Project is derived from cwd via Path.GetFileName — same logic as lead path
        const string sid = "test-teammate-before-lead-03";
        var json = BuildJson("PostToolUse", sid, cwd: "/home/u/proj", agentId: "agent-xyz");
        var env = new RecordingEnv();

        HookCommand.Run(_services, new StringReader(json), env);

        var reader = _services.GetRequiredService<StateFileReader>();
        var state = reader.ReadStateFile(StateFilePath(sid));
        state!.Project.Should().Be("proj",
            "Project must be the last path segment of the normalized cwd, matching the lead path derivation");
    }
}

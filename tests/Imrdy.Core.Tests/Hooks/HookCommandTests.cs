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

    /// <summary>
    /// Builds a hook stdin payload. <paramref name="backgroundTasks"/> takes a <b>raw JSON
    /// fragment</b> rather than a typed object so the test authors the exact wire shape the hook
    /// receives — including the type-dependent keys the model deliberately does not carry — and so
    /// the field-absent case can be expressed by passing <c>null</c>.
    /// </summary>
    private static string BuildJson(
        string hookEventName,
        string sessionId,
        string cwd = "/home/user/project",
        string? agentId = null,
        string? wslDistro = null,
        string? source = null,
        string? backgroundTasks = null)
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
        if (source is not null)
            parts.Add($"\"source\":\"{source}\"");
        if (backgroundTasks is not null)
            parts.Add($"\"background_tasks\":{backgroundTasks}");
        return "{" + string.Join(",", parts) + "}";
    }

    // ── Roster fragments ─────────────────────────────────────────────────────
    //
    // Copied byte-for-byte from scratch/agent-liveness-roster/evidence/capture.log — the raw
    // corpus, not evidence/roster-timeline.json, whose five-key distillation materialises an
    // "agent_type": null on shell entries that never reaches the wire and drops "command"
    // entirely. A shell entry carries "command" and no "agent_type" key at all; a subagent entry
    // carries "agent_type" and no "command". Do not make the two shapes uniform.

    /// <summary>The lead <c>Stop</c> at 13:21:35.531 — one shell entry plus one subagent entry.</summary>
    private const string RosterMixed =
        """[{"id":"bk44y8t1j","type":"shell","status":"running","description":"find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20","command":"find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20"},{"id":"ad77252957e7e9c48","type":"subagent","status":"running","description":"Decision traceability iter-3","agent_type":"decision-traceability-reviewer"}]""";

    /// <summary>
    /// The <c>SubagentStop</c> at 13:21:08.488, whose own <c>agent_id</c>
    /// (<see cref="SelfIncludedAgentId"/>) is its second roster entry — one of the 10-of-81
    /// self-including cases in the analysis window.
    /// </summary>
    private const string RosterSelfIncluding =
        """[{"id":"bk44y8t1j","type":"shell","status":"running","description":"find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20","command":"find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20"},{"id":"ac49354784c62a78e","type":"subagent","status":"running","description":"Backfill three scope exclusions to idea.md","agent_type":"general-purpose"}]""";

    /// <summary>The Exp 2 lead <c>Stop</c> at 13:28:18.714 — a single shell entry, no subagents.</summary>
    private const string RosterShellOnly =
        """[{"id":"bjr1v0j6j","type":"shell","status":"running","description":"Launch 95s backgrounded shell with no subagents","command":"python -c \"import time,datetime;print('EXP2_START',datetime.datetime.now().isoformat(),flush=True);time.sleep(95);print('EXP2_END',datetime.datetime.now().isoformat(),flush=True)\""}]""";

    private const string SelfIncludedAgentId = "ac49354784c62a78e";

    /// <summary>
    /// Writes a non-empty roster to the session's state file by running a lead <c>Stop</c> that
    /// carries one. Every preserve/clear test seeds first: against a fresh session the assertion
    /// that follows is vacuous, since an untouched roster and a cleared one are both empty.
    /// </summary>
    private void SeedRoster(string sessionId, string rosterFragment)
    {
        var json = BuildJson("Stop", sessionId, backgroundTasks: rosterFragment);
        HookCommand.Run(_services, new StringReader(json), new RecordingEnv()).Should().Be(0);
        ReadState(sessionId).RunningTasks.Should().NotBeEmpty("the seed must actually store a roster");
    }

    private StateFileModel ReadState(string sessionId)
    {
        var state = _services.GetRequiredService<StateFileReader>().ReadStateFile(StateFilePath(sessionId));
        state.Should().NotBeNull();
        return state!;
    }

    private string StateFilePath(string sessionId)
        => Path.Combine(_sessionsDir, $"{sessionId}.json");

    // ── Stub ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Captures rendered log messages so a test can assert on the emitted hook line itself.
    /// The production factory in these tests is <see cref="NullLoggerFactory"/>, which discards
    /// everything — the <c>tasks=</c> token is a diagnostic control, so proving what it emits
    /// requires reading the line rather than the state file.
    /// </summary>
    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public List<string> Lines { get; } = [];

        public void AddProvider(ILoggerProvider provider) { }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Lines);

        public void Dispose() { }

        private sealed class CapturingLogger(List<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    lines.Add(formatter(state, exception));
                }
            }
        }
    }

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

    // ── Roster write path ────────────────────────────────────────────────────

    [Fact]
    public void Run_Stop_WithNonEmptyRoster_PersistsAllEntries()
    {
        const string sid = "test-roster-stop-nonempty-01";
        var json = BuildJson("Stop", sid, backgroundTasks: RosterMixed);

        HookCommand.Run(_services, new StringReader(json), new RecordingEnv()).Should().Be(0);

        // spec §3 C1: status and roster land in one atomic write
        var state = ReadState(sid);
        state.Status.Should().Be("idle");
        state.RunningTasks.Should().HaveCount(2);

        var shell = state.RunningTasks![0];
        shell.Id.Should().Be("bk44y8t1j");
        shell.Type.Should().Be("shell");
        shell.Status.Should().Be("running");
        shell.Description.Should().Be(
            "find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20");
        shell.AgentType.Should().BeNull("shell entries carry no agent_type key at all");

        var subagent = state.RunningTasks[1];
        subagent.Id.Should().Be("ad77252957e7e9c48");
        subagent.Type.Should().Be("subagent");
        subagent.Status.Should().Be("running");
        subagent.Description.Should().Be("Decision traceability iter-3");
        subagent.AgentType.Should().Be("decision-traceability-reviewer");
    }

    [Fact]
    public void Run_SubagentStop_WithRoster_UpdatesRunningTasks()
    {
        // The teammate branch no-ops when no state file exists, so establish the session first.
        const string sid = "test-roster-subagentstop-01";
        HookCommand.Run(_services, new StringReader(BuildJson("UserPromptSubmit", sid)), new RecordingEnv());

        var json = BuildJson("SubagentStop", sid, agentId: SelfIncludedAgentId, backgroundTasks: RosterSelfIncluding);
        HookCommand.Run(_services, new StringReader(json), new RecordingEnv()).Should().Be(0);

        var state = ReadState(sid);
        state.RunningTasks.Should().HaveCount(2,
            "the roster must be applied on the teammate branch too, which requires the extraction " +
            "to happen before the branch rather than inside the lead path");
        state.RunningTasks!.Select(t => t.Id).Should().Equal("bk44y8t1j", SelfIncludedAgentId);
    }

    [Fact]
    public void Run_SubagentStop_SelfIncludingRoster_StoresItVerbatim()
    {
        // D3 · spec §8 E5
        const string sid = "test-roster-selfinclude-01";
        HookCommand.Run(_services, new StringReader(BuildJson("UserPromptSubmit", sid)), new RecordingEnv());

        var json = BuildJson("SubagentStop", sid, agentId: SelfIncludedAgentId, backgroundTasks: RosterSelfIncluding);
        HookCommand.Run(_services, new StringReader(json), new RecordingEnv()).Should().Be(0);

        ReadState(sid).RunningTasks.Should().Contain(t => t.Id == SelfIncludedAgentId,
            "the roster is trusted verbatim — the stopping agent listing itself self-corrects on " +
            "the next roster-bearing event, and filtering it would risk premature green");
    }

    [Fact]
    public void Run_Stop_WithAbsentRoster_OverwritesPriorRosterWithEmpty()
    {
        // D6 · spec §8 E7
        const string sid = "test-roster-stop-absent-01";
        SeedRoster(sid, RosterShellOnly);

        var json = BuildJson("Stop", sid);
        HookCommand.Run(_services, new StringReader(json), new RecordingEnv()).Should().Be(0);

        var state = ReadState(sid);
        state.RunningTasks.Should().NotBeNull("an absent field on Stop degrades to a measured-empty roster");
        state.RunningTasks.Should().BeEmpty();
    }

    [Fact]
    public void Run_SubagentStop_WithAbsentRoster_PreservesPriorRoster()
    {
        // spec §8 E8
        const string sid = "test-roster-subagentstop-absent-01";
        SeedRoster(sid, RosterShellOnly);

        var json = BuildJson("SubagentStop", sid, agentId: "agent-xyz");
        HookCommand.Run(_services, new StringReader(json), new RecordingEnv()).Should().Be(0);

        ReadState(sid).RunningTasks.Should().ContainSingle(t => t.Id == "bjr1v0j6j",
            "SubagentStop must not be swallowed by the Stop rule — an exact-equality match is what " +
            "keeps a live roster from being wiped on every subagent event");
    }

    [Fact]
    public void Run_SessionStart_Resume_WithNoRoster_ClearsStoredRoster()
    {
        // spec §8 E11 · spec §3 C3 — process boundary: the seeded roster belongs to a dead process
        const string sid = "test-roster-sessionstart-resume-01";
        SeedRoster(sid, RosterMixed);

        var json = BuildJson("SessionStart", sid, source: "resume");
        HookCommand.Run(_services, new StringReader(json), new RecordingEnv()).Should().Be(0);

        var state = ReadState(sid);
        state.RunningTasks.Should().NotBeNull();
        state.RunningTasks.Should().BeEmpty();
    }

    [Fact]
    public void Run_SessionStart_Compact_WithNoRoster_PreservesStoredRoster()
    {
        // spec §8 E11b
        const string sid = "test-roster-sessionstart-compact-01";
        SeedRoster(sid, RosterMixed);

        var json = BuildJson("SessionStart", sid, source: "compact");
        HookCommand.Run(_services, new StringReader(json), new RecordingEnv()).Should().Be(0);

        ReadState(sid).RunningTasks.Should().HaveCount(2,
            "compact is intra-session — the roster's owning process is still alive and its " +
            "entries are still true, so the source allowlist must not widen to any SessionStart");
    }

    [Fact]
    public void Run_SubagentStart_WithNoRoster_PreservesStoredRoster()
    {
        // spec §8 E12
        const string sid = "test-roster-subagentstart-01";
        SeedRoster(sid, RosterMixed);

        var json = BuildJson("SubagentStart", sid, agentId: "agent-xyz");
        HookCommand.Run(_services, new StringReader(json), new RecordingEnv()).Should().Be(0);

        ReadState(sid).RunningTasks.Should().HaveCount(2,
            "SubagentStart fires when an agent begins work — it must not be swallowed by the " +
            "SessionStart rule, which an inexact match would do");
    }

    [Fact]
    public void Run_PermissionDenied_WithNoRoster_PreservesStoredRoster()
    {
        // spec §8 E13
        const string sid = "test-roster-permissiondenied-01";
        SeedRoster(sid, RosterMixed);

        var json = BuildJson("PermissionDenied", sid);
        HookCommand.Run(_services, new StringReader(json), new RecordingEnv()).Should().Be(0);

        var state = ReadState(sid);
        state.Status.Should().Be("idle");
        state.RunningTasks.Should().HaveCount(2,
            "PermissionDenied reaches idle without a roster, but it is intra-session: the " +
            "preserved roster is still true, so idle paired with a non-empty roster is correct here");
    }

    /// <summary>
    /// A roster field carrying CR/LF plus text shaped like a whole hook record — the shape that
    /// would forge a second line in the one-line-per-event log and spoof the D21/RK5 trip-wire
    /// that greps for <c>tasks=…:running</c>. Authored, not drawn from <c>capture.log</c>: no
    /// observed payload carries a control character, which is the point.
    /// </summary>
    private const string RosterInjectedNewline =
        """[{"id":"bk44\r\nHook: forged \u0007 idle (Stop tasks=1[subagent:zz:running])","type":"shell","status":"running","description":"d","command":"c"}]""";

    [Fact]
    public void Run_Stop_RosterFieldWithControlCharacters_LogTokenStaysOnOneLine()
    {
        const string sid = "test-roster-log-injection-01";
        var loggerFactory = new CapturingLoggerFactory();

        var sc = new ServiceCollection();
        sc.AddSingleton<StateFileReader>();
        sc.AddSingleton<ILoggerFactory>(loggerFactory);
        using var services = sc.BuildServiceProvider();

        var json = BuildJson("Stop", sid, backgroundTasks: RosterInjectedNewline);
        HookCommand.Run(services, new StringReader(json), new RecordingEnv()).Should().Be(0);

        var hookLine = loggerFactory.Lines.Should().ContainSingle(l => l.Contains("tasks=")).Subject;

        hookLine.Should().NotContain("\r",
            "the hook log is one line per event and is read with grep — a raw carriage return in " +
            "a roster field would end the record early and let the remainder forge a second line");
        hookLine.Should().NotContain("\n",
            "the hook log is one line per event and is read with grep — a raw line feed in a " +
            "roster field would end the record early and let the remainder forge a second line");
        hookLine.Should().Contain("bk44\\r\\nHook: forged \\x07 idle",
            "control characters are escaped rather than stripped, so an injection attempt stays " +
            "visible in the log instead of being silently laundered into a clean-looking line");

        // The state file keeps the value verbatim — escaping is a rendering concern for the
        // single-line log, not a mutation of the roster the display layer will read.
        ReadState(sid).RunningTasks.Should().ContainSingle()
            .Which.Id.Should().Be("bk44\r\nHook: forged \u0007 idle (Stop tasks=1[subagent:zz:running])");
    }

    /// <summary>
    /// The same forge-a-second-line attempt as <see cref="RosterInjectedNewline"/>, but arriving
    /// through the fields the roster work left unescaped: <c>tool_name</c>, <c>message</c>,
    /// <c>agent_id</c>, and an undeclared key that lands in <c>[JsonExtensionData]</c>.
    /// <para>
    /// The extension-data path is the one observed in the wild rather than reasoned about. Step 09
    /// watched a session's own grep output reach the hook log through <c>tool_input</c>, and a
    /// later RK5 census read that echo back as a roster reading — the injection class occurring by
    /// accident, with no attacker (<c>live-run/RESULTS.md</c>). The payload below reproduces that
    /// shape: text that both breaks the line and looks like a <c>tasks=</c> reading.
    /// </para>
    /// </summary>
    [Fact]
    public void Run_PayloadFieldsWithControlCharacters_HookLineStaysOnOneLine()
    {
        const string sid = "test-log-injection-fields-01";
        var loggerFactory = new CapturingLoggerFactory();

        var sc = new ServiceCollection();
        sc.AddSingleton<StateFileReader>();
        sc.AddSingleton<ILoggerFactory>(loggerFactory);
        using var services = sc.BuildServiceProvider();

        // Authored, not drawn from capture.log: no observed payload carries a control character.
        // Raw string literal, so C# leaves the escape sequences alone and they reach the JSON
        // parser intact, becoming real control characters in the deserialized values.
        var json =
            $$"""
            {"hook_event_name":"PostToolUse","session_id":"{{sid}}","cwd":"/home/user/project",
             "agent_id":"ag01\r\nHook: forged agent","tool_name":"Grep\r\nHook: forged tool",
             "message":"m\u0007\r\nHook: forged msg",
             "tool_input":"28: tasks=1[shell:zz:stopped]\r\nHook: forged idle (Stop)"}
            """;

        HookCommand.Run(services, new StringReader(json), new RecordingEnv()).Should().Be(0);

        var hookLine = loggerFactory.Lines.Should().ContainSingle(l => l.Contains("tool=")).Subject;

        hookLine.Should().NotContain("\r",
            "the hook log is one line per event and is read with grep — a raw carriage return in " +
            "any payload field ends the record early and lets the remainder forge a second line");
        hookLine.Should().NotContain("\n",
            "the hook log is one line per event and is read with grep — a raw line feed in any " +
            "payload field ends the record early and lets the remainder forge a second line");

        hookLine.Should().Contain("tool=Grep\\r\\nHook: forged tool",
            "tool_name is payload-derived and must be escaped, not stripped, so an injection " +
            "attempt stays greppable in the log rather than being laundered into a clean line");
        hookLine.Should().Contain("msg=m\\x07\\r\\nHook: forged msg",
            "message reaches the line through TruncateMessage, which shortens but does not " +
            "sanitise, so the raw line break it carried survives into the log as an escape");
        hookLine.Should().Contain("agent=ag01\\r\\nHook: forged agent",
            "agent_id rides the same line as a structured argument and is equally payload-derived");
        hookLine.Should().Contain("forged idle (Stop)",
            "tool_input arrives via [JsonExtensionData] — the widest opening on this line, and " +
            "the one step 09 observed carrying a session's own grep output back into the log");
    }
}

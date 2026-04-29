using FluentAssertions;
using Imrdy.Core.Hooks;

namespace Imrdy.Core.Tests.Hooks;

public class IHookEnvironmentTests
{
    private sealed class LinuxStyleStub : IHookEnvironment
    {
        public int? ResolveTerminalPid(int currentPid, string sessionId) => null;
        public void EnsureTrayRunning() { }
        public string NormalizeCwd(string? cwd) => cwd ?? "";
        public void OnSessionEnd(string sessionId) { }
        public string? GetWslDistro() => "TestDistro-1.0";
    }

    [Fact]
    public void LinuxStub_ImplementsAllFourMethods()
    {
        IHookEnvironment env = new LinuxStyleStub();

        env.ResolveTerminalPid(1234, "sid").Should().BeNull();
        env.EnsureTrayRunning(); // no-op — no throw is the contract
        env.NormalizeCwd("/home/foo").Should().Be("/home/foo");
        env.OnSessionEnd("sid"); // no-op — no throw is the contract
        env.GetWslDistro().Should().Be("TestDistro-1.0");
    }

    [Fact]
    public void LinuxStub_GetWslDistro_ReturnsConfiguredValue()
    {
        IHookEnvironment env = new LinuxStyleStub();

        env.GetWslDistro().Should().Be("TestDistro-1.0");
    }

    [Fact]
    public void LinuxStub_NormalizeCwd_ReturnsVerbatimLinuxPath()
    {
        IHookEnvironment env = new LinuxStyleStub();

        env.NormalizeCwd("/home/foo").Should().Be("/home/foo");
    }

    [Fact]
    public void LinuxStub_NormalizeCwd_NullReturnsEmpty()
    {
        IHookEnvironment env = new LinuxStyleStub();

        env.NormalizeCwd(null).Should().Be("");
    }
}

using FluentAssertions;
using Imrdy.Core.Publishing;
using Imrdy.Core.State;

namespace Imrdy.Core.Tests.Publishing;

public class RemoteDesktopDefaultTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static StateFileModel Session(string id, string? origin, int? desktop, int minutes) => new()
    {
        SessionId = id,
        Status = "idle",
        Project = "p",
        Cwd = @"D:\dev\p",
        HookEvent = "Stop",
        OriginMachine = origin,
        DesktopIndex = desktop,
        Timestamp = T0.AddMinutes(minutes),
    };

    [Fact]
    public void Resolve_SiblingsFromSameMachine_TakesMostRecentlyActiveDesktop()
    {
        var sessions = new[]
        {
            Session("old", "PC-EXCALIBUR", 2, minutes: 0),
            Session("recent", "pc-excalibur", 14, minutes: 10),
        };

        RemoteDesktopDefault.Resolve("PC-EXCALIBUR", "new", sessions, currentDesktop: 3).Should().Be(14);
    }

    [Fact]
    public void Resolve_IgnoresOtherMachinesSelfAndUnassignedSiblings()
    {
        var sessions = new[]
        {
            Session("new", "PC-EXCALIBUR", 9, minutes: 30),
            Session("unassigned", "PC-EXCALIBUR", null, minutes: 20),
            Session("other-machine", "build-box", 7, minutes: 10),
            Session("local", null, 5, minutes: 10),
        };

        RemoteDesktopDefault.Resolve("PC-EXCALIBUR", "new", sessions, currentDesktop: 3).Should().Be(3);
    }

    [Fact]
    public void Resolve_NoSiblingsAndNoCurrentDesktop_ReturnsNull()
    {
        RemoteDesktopDefault.Resolve("PC-EXCALIBUR", "new", [], currentDesktop: null).Should().BeNull();
    }
}

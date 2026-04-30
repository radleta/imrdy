using FluentAssertions;
using Imrdy.Core.Desktop;
using Imrdy.Core.State;
using Imrdy.Windows;
using Xunit;

namespace Imrdy.Windows.Tests.TrayApp;

/// <summary>
/// Unit tests for WSL desktop-capture logic.
/// Tests target <see cref="WslDesktopCapture"/> — the pure static helper extracted
/// from TrayApp so full WinForms instantiation is not required.
/// </summary>
public class DesktopCaptureWslTests
{
    private static StateFileModel BuildState(
        string hookEvent,
        string? wslDistro,
        int? desktopIndex) => new()
    {
        SessionId = "test-session",
        Status = "start",
        Project = "test-project",
        Cwd = "/home/user/project",
        HookEvent = hookEvent,
        Timestamp = DateTimeOffset.UtcNow,
        WslDistro = wslDistro,
        DesktopIndex = desktopIndex,
    };

    [Fact]
    public void MaybeStampDesktopIndex_WslSessionStart_NullIndex_StampsAndWrites()
    {
        var state = BuildState("SessionStart", "Ubuntu-22.04", desktopIndex: null);
        string? writtenPath = null;
        StateFileModel? writtenModel = null;

        var result = WslDesktopCapture.MaybeStampDesktopIndex(
            state,
            filePath: "/wsl/sessions/test.json",
            desktopManager: new StubDesktopManager(returnedIndex: 2),
            writeFile: (path, model) => { writtenPath = path; writtenModel = model; });

        result.DesktopIndex.Should().Be(2);
        writtenPath.Should().Be("/wsl/sessions/test.json");
        writtenModel.Should().NotBeNull();
        writtenModel!.DesktopIndex.Should().Be(2);
    }

    [Fact]
    public void MaybeStampDesktopIndex_WindowsNativeSession_NeverStamps()
    {
        var state = BuildState("SessionStart", wslDistro: null, desktopIndex: null);
        var writeCallCount = 0;

        var result = WslDesktopCapture.MaybeStampDesktopIndex(
            state,
            filePath: "/local/sessions/test.json",
            desktopManager: new StubDesktopManager(returnedIndex: 1),
            writeFile: (_, _) => writeCallCount++);

        result.DesktopIndex.Should().BeNull();
        writeCallCount.Should().Be(0);
    }

    [Fact]
    public void MaybeStampDesktopIndex_WslSessionStart_ExistingDesktopIndex_NoReStamp()
    {
        var state = BuildState("SessionStart", "Ubuntu-22.04", desktopIndex: 3);
        var writeCallCount = 0;

        var result = WslDesktopCapture.MaybeStampDesktopIndex(
            state,
            filePath: "/wsl/sessions/test.json",
            desktopManager: new StubDesktopManager(returnedIndex: 5),
            writeFile: (_, _) => writeCallCount++);

        result.DesktopIndex.Should().Be(3);
        writeCallCount.Should().Be(0);
    }

    // Minimal stub — no mocking framework needed for these three cases.
    private sealed class StubDesktopManager(int? returnedIndex) : IDesktopManager
    {
        public bool IsAvailable => returnedIndex.HasValue;
        public int? GetCurrentDesktopIndex() => returnedIndex;
        public int? GetDesktopForWindow(IntPtr hwnd) => null;
        public void SwitchToDesktop(int index) { }
        public void FocusWindow(IntPtr hwnd) { }
        public int? GetDesktopCount() => null;
        public void Reinitialize() { }
        public void PinWindowToAllDesktops(IntPtr hwnd) { }
        public void Dispose() { }
    }
}

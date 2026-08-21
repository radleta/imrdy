using FluentAssertions;
using Imrdy.Windows.Desktop;
using Xunit;

namespace Imrdy.Windows.Tests.Desktop;

/// <summary>
/// Unit tests for <see cref="PInvokeWindow.IsAcceptableForegroundCandidate"/> — the pure
/// decision logic behind capture-time foreground-restore-candidate validation
/// (TrayApp.CaptureForegroundForRestore). Deliberately does not touch a live HWND: the
/// live Win32 queries (IsWindow, GetWindowThreadProcessId, HasCaptionStyle) that feed this
/// predicate are exercised only indirectly, through manual live testing, since they require
/// a real window handle.
/// </summary>
public class PInvokeWindowTests
{
    [Fact]
    public void IsAcceptableForegroundCandidate_ValidOtherProcessWindowWithCaption_ReturnsTrue()
    {
        // The common case: the user's terminal — a real top-level window, in a different
        // process, with a caption.
        PInvokeWindow.IsAcceptableForegroundCandidate(
            isValidWindow: true, isOwnProcess: false, hasCaption: true)
            .Should().BeTrue();
    }

    [Fact]
    public void IsAcceptableForegroundCandidate_NotAWindow_ReturnsFalse()
    {
        // GetForegroundWindow() returned IntPtr.Zero, or the handle no longer resolves —
        // nothing to restore to.
        PInvokeWindow.IsAcceptableForegroundCandidate(
            isValidWindow: false, isOwnProcess: false, hasCaption: true)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAcceptableForegroundCandidate_OwnProcess_ReturnsFalse()
    {
        // The observed failure mode: right-clicking while a previous menu's transient
        // ToolStripDropDown (imrdy's own process) is still closing captures that popup
        // instead of the user's real window.
        PInvokeWindow.IsAcceptableForegroundCandidate(
            isValidWindow: true, isOwnProcess: true, hasCaption: true)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAcceptableForegroundCandidate_NoCaption_ReturnsFalse()
    {
        // A borderless popup from another process (tooltip, flyout, another app's own
        // context menu) — not a "real" top-level app window worth restoring to.
        PInvokeWindow.IsAcceptableForegroundCandidate(
            isValidWindow: true, isOwnProcess: false, hasCaption: false)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAcceptableForegroundCandidate_OwnProcessAndNoCaption_ReturnsFalse()
    {
        // Both rejection reasons at once — still rejected, not a special/double-negative case.
        PInvokeWindow.IsAcceptableForegroundCandidate(
            isValidWindow: true, isOwnProcess: true, hasCaption: false)
            .Should().BeFalse();
    }

    [Fact]
    public void IsAcceptableForegroundCandidate_InvalidWindowOverridesOtherFlags_ReturnsFalse()
    {
        // isValidWindow=false must reject regardless of what the (meaningless, in that case)
        // other flags say.
        PInvokeWindow.IsAcceptableForegroundCandidate(
            isValidWindow: false, isOwnProcess: false, hasCaption: false)
            .Should().BeFalse();
    }
}

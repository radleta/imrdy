---
tags: [imrdy-expert/testing]
summary: "MenuRenderer.Apply asserts Application.MessageLoop, which a bare STA thread does not satisfy — ContextMenuStrip.Opening tests need a real Application.Run pump or the assert is swallowed as a misleading zero-items failure"
last-verified: "2026-09-25"
---

## WinForms Menu Tests

Testing `MenuRenderer.Apply`, or any `ContextMenuStrip.Opening` rebuild, needs a running `Application.Run` pump — an STA thread alone is not enough.

`MenuRenderer.Apply` (`src/Imrdy.Windows/Menus/MenuRenderer.cs`) opens with
`Debug.Assert(Application.MessageLoop, "MenuRenderer.Apply must be called on the UI thread")`.
`Application.MessageLoop` is only `true` while a genuine `Application.Run(...)` pump is
actively executing on the calling thread — a bare STA `Thread` with no pump running (the
shape `InspectServiceTests.RunOnSta` uses for its offscreen `Form.Show()` + walk, which needs
no such pump) does **not** satisfy it. Calling `menu.Show(owner, point)` on such a thread
still synchronously reaches the `Opening` handler and `MenuRenderer.Apply`, but the assert
fails; the test host converts `Debug.Fail` into a `DebugAssertException` rather than a modal
dialog, and every one of this project's menu builders wraps its rebuild in a
try/catch-and-log-warning — so the exception is silently swallowed, leaving `menu.Items.Count
== 0` and `e.Cancel` untouched. The result is a misleading zero-items failure that looks
identical to the first-show `Cancel` defect `MenuOpeningPolicy` fixes (see
[Overlay Context Menus](../overlay-context-menus.md)) but is not it; only capturing the logger
output shows the assert.

**Working pattern:** run the test body inside an actual `Application.Run(ApplicationContext)`
pump on the STA thread, dispatched via a one-shot `System.Windows.Forms.Timer` tick (`Interval
= 1`), calling `appContext.ExitThread()` when the test body finishes. This mirrors how
production reaches `TrayApp.ShowContextMenuAt` — synchronously inside a message already being
pumped by the running app — and satisfies `Application.MessageLoop` for the duration of the
test body. See
[`MenuOpeningEndToEndTests.cs` `RunOnSta`](../../../../tests/Imrdy.Windows.Tests/Menus/MenuOpeningEndToEndTests.cs)
for the concrete implementation.

Use the simpler bare-STA-thread `Form.Show()` harness only for layout and walker tests like
`InspectServiceTests`; anything that drives a `ContextMenuStrip.Opening` handler wired through
`MenuRenderer.Apply`, or anything else asserting `Application.MessageLoop`, needs the pump.

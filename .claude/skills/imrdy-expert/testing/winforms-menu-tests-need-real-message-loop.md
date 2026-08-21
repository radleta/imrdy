---
tags: [imrdy-expert/testing]
summary: "MenuRenderer.Apply asserts Application.MessageLoop, which a bare STA thread does not satisfy — ContextMenuStrip.Opening tests need a real Application.Run pump or the assert is swallowed as a misleading zero-items failure"
---

## Testing MenuRenderer.Apply (or Any ContextMenuStrip.Opening Rebuild) Needs a Running Application.Run Pump, Not Just an STA Thread

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
== 0` and `e.Cancel` untouched. This produces a *different*, misleading zero-items test
failure that looks identical to (but is not) the real defect under test.

**Working pattern:** run the test body inside an actual `Application.Run(ApplicationContext)`
pump on the STA thread, dispatched via a one-shot `System.Windows.Forms.Timer` tick (`Interval
= 1`), calling `appContext.ExitThread()` when the test body finishes. This mirrors how
production actually reaches `TrayApp.ShowContextMenuAt` — synchronously inside a message
already being pumped by the running app — and satisfies `Application.MessageLoop` for the
duration of the test body. See
`tests/Imrdy.Windows.Tests/Menus/MenuOpeningEndToEndTests.cs`'s `RunOnSta` for the concrete
implementation.

**Discovered:** Step 8, writing live-`ContextMenuStrip` regression coverage for the
first-right-click-eaten fix — the first attempt using `InspectServiceTests`'s existing
bare-STA-thread harness produced a confusing `Items.Count == 0` failure that took direct
`ILogger` capture to diagnose as `MenuRenderer.Apply`'s own defensive assert firing, not the
regression the test was meant to catch.
**Impact:** Any future test that exercises a `ContextMenuStrip.Opening` handler wired through
`MenuRenderer.Apply` (or anything else asserting `Application.MessageLoop`) needs the
`Application.Run`-pump harness, not the simpler bare-STA-thread `Form.Show()` harness that
suffices for layout/walker tests like `InspectServiceTests`.

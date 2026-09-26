---
tags: [imrdy-expert/overlay]
summary: "How the overlay's right-click ContextMenuStrip opens reliably and hands focus back: MA_NOACTIVATE always, an explicit SetForegroundWindow + InvokeWithForegroundAttached grant, PostMessage(WM_NULL) per KB135788, and a continuously sampled restore target"
last-verified: "2026-09-25"
---

# Overlay Context Menus

A right-click on the overlay opens a `ContextMenuStrip` through `router.OpenSessionMenu` / `OpenWorkspaceMenu` / `OpenOverlayMenu` with `MenuAnchor.AtControl`, and every one of those lands in `TrayApp.ShowContextMenuAt`. Three things make that menu open on the first click, stay open, and give focus back to the user's terminal when it closes. Each was added against a live failure; removing any one brings its symptom back.

## The overlay never activates itself

`OverlayPanel.WndProc` returns `MA_NOACTIVATE` for every `WM_MOUSEACTIVATE`, right-click included — there is no per-button exception. Returning `MA_ACTIVATE` for a right-button-down, to give the menu a real foreground owner, was tried and did not work:

- **Activation is not foreground input.** `MA_ACTIVATE` makes Windows activate the window, but `SetForegroundWindow` and `ContextMenuStrip.Show`'s own foreground handling still require the calling thread to own foreground input rights. Measured live: 4 of 9 right-clicks silently no-opped (`menu.Visible=false` after `Show`) and 4 of 5 focus restores failed.
- **It destroys the restore target.** Windows completes the activation while handling `WM_MOUSEACTIVATE`, synchronously, before the triggering `WM_RBUTTONDOWN` is delivered — so `OnMouseDown`/`OnMouseUp` run after the switch. Once the overlay activates itself, `GetForegroundWindow()` in `OnMouseUp` or `ShowContextMenuAt` returns the overlay, and the user's previous window is no longer observable.

## The explicit foreground grant

`ShowContextMenuAt`'s `AtControl` branch does, in order:

1. `CaptureForegroundForRestore()` — record the window to give focus back to.
2. Inside `PInvokeWindow.InvokeWithForegroundAttached` (the `AttachThreadInput` dance the tray-icon path's `NotifyIconMenuHost` uses): `SetForegroundWindow(owner.Handle)`, then `menu.Show(owner, location)`, then `PostMessage(owner.Handle, WM_NULL, 0, 0)`.
3. On the menu's `Closed`: `RestorePendingForeground()`, whose `SetForegroundWindow` is wrapped in the same `InvokeWithForegroundAttached`.

The `WM_NULL` post is Microsoft KB135788: when the owner is already foreground, a popup menu "appears and immediately disappears on the second display", and posting `WM_NULL` forces the task switch that prevents it. Without it the menu alternates open / not-open on consecutive right-clicks — 13 consecutive shows in one tray log alternated almost perfectly. It had been implemented once before and removed by a reader who took `SetForegroundWindow` + `Show` to be the whole fix; the inline KB135788 comment in `ShowContextMenuAt` and the `MenuAnchor.AtControl` doc comment exist to stop that happening again. Source: [a Microsoft Q&A thread quoting KB135788](https://learn.microsoft.com/en-us/answers/questions/1125620/resolved-maui-trayicon-with-contextmenustrip-not-c).

`ShowContextMenuAt` is the single routing function: `NotifyIconMenuHost` and `menu.Show` are not called anywhere else.

## The continuously sampled restore target

Capturing the foreground window at menu-show time is usually too late: by the click, some other imrdy window (a dashboard, a previous menu's transient popup) has often taken foreground, and the capture is rejected as own-process. So `SampleForegroundForRestoreTracking()`, run from the existing 100ms drain tick (no new timer), keeps `_lastGoodForegroundWindow` updated with the latest window that passes `PInvokeWindow.IsAcceptableForegroundCandidate` (valid, not this process, has a caption). `CaptureForegroundForRestore` still checks the click-time window first, as the freshest signal when the click itself is the first legitimate change since the last tick, and falls back to the sampled value — which is what powers the restore in the common case.

## First show of a freshly built menu

A separate defect presented the same way — "the first right-click is eaten, every later one works" — and is fixed elsewhere: `ContextMenuStrip.OnOpening` pre-sets `e.Cancel = true` when `Items.Count == 0`, before raising `Opening`, and every menu builder populates its items inside its own `Opening` handler. Each builder clears the flag through `MenuOpeningPolicy.ShouldClearCancel` after a successful rebuild. Testing that path needs a real message loop — see [WinForms Menu Tests](testing/winforms-menu-tests.md).

## Related

- [Hover Dashboard State Machine](hover-dashboard-state-machine.md) — why right-click does not raise `SurfaceInteracted`
- [Overlay Interactivity](overlay-interactivity.md) — the overlay's event contract

---
tags: [imrdy-expert/overlay]
summary: "AtControl context menus get foreground from an explicit SetForegroundWindow + InvokeWithForegroundAttached dance, not a WM_MOUSEACTIVATE exception — WndProc still returns MA_NOACTIVATE unconditionally"
---

## Overlay Context-Menu Foreground Is Granted by an Explicit SetForegroundWindow Dance, Not a WM_MOUSEACTIVATE Exception

`OverlayPanel.WndProc` returns `MA_NOACTIVATE` unconditionally for every `WM_MOUSEACTIVATE`,
including right-click — there is no per-message-type activation exception. This was a
reversal of a step-3 attempt (see `step-03-mouseactivate-precedes-buttondown.md`) that
returned `MA_ACTIVATE` for a right-button-down specifically, to give the AtControl-anchored
`ContextMenuStrip` (opened via `MenuAnchor.AtControl`) a real foreground owner so
`ToolStripManager`'s modal menu filter wouldn't force-close it.

That attempt did not fully work: live-measured evidence showed 4 of 9 right-clicks still
silently no-opped (`menu.Show returned, menu.Visible=false`) and 4 of 5 foreground restores
failed (`captured target is no longer a valid window`). The root mechanical issue: returning
`MA_ACTIVATE` from `WM_MOUSEACTIVATE` makes Windows *activate* the window, but activation is
not the same thing as owning foreground *input* — `SetForegroundWindow` and
`ContextMenuStrip.Show`'s internal foreground handling both additionally require the calling
thread to already own foreground input rights, which `MA_ACTIVATE` alone does not grant.

The working fix instead reuses the AttachThreadInput dance this codebase already had for the
tray-icon menu path (`NotifyIconMenuHost`, extracted into
`PInvokeWindow.InvokeWithForegroundAttached`): `TrayApp.ShowContextMenuAt`'s `AtControl`
branch calls `PInvokeWindow.SetForegroundWindow(owner.Handle)` explicitly, wrapped in
`InvokeWithForegroundAttached`, immediately before `menu.Show(owner, location)` — and the
same wrapping is applied to `RestorePendingForeground`'s `SetForegroundWindow` call once the
menu closes. Because the overlay's own `WM_MOUSEACTIVATE` handler never activates it, the
foreground window at the moment `ShowContextMenuAt` captures it (via
`CaptureForegroundForRestore`, before the explicit grant) is still the user's real prior
window (e.g. their terminal) — no special earlier capture point inside `WndProc` is needed
anymore, which is what made the step-3 approach fragile in the first place.

**Discovered:** During step 5, when re-evaluating whether the step-3 `MA_ACTIVATE` policy was
still needed once the explicit foreground-attach dance was added. Decided to remove it rather
than keep both mechanisms layered.
**Impact:** Restores the project's documented invariant ("WM_MOUSEACTIVATE always returns
MA_NOACTIVATE — the overlay never steals foreground") to be actually true unconditionally,
not true-except-for-right-click. Any future overlay interaction that needs the overlay (or its
owner) to briefly hold real foreground should reuse
`PInvokeWindow.InvokeWithForegroundAttached` + explicit `SetForegroundWindow`, not a
`WM_MOUSEACTIVATE` activation exception.

## KB135788 WM_NULL and Continuous Foreground Sampling (step 6 correction)

Step 5's `SetForegroundWindow` + `InvokeWithForegroundAttached` dance (documented on this same
page) fixed most of the AtControl-anchored `ContextMenuStrip`'s foreground problems, but two
things it implicitly assumed turned out to be incomplete in practice, discovered via live logs
from `~/.imrdy/logs/monitor_940.log`:

1. **`menu.Show` alone still needs `PostMessage(owner.Handle, WM_NULL, 0, 0)` immediately after
   it, per Microsoft KB135788.** Without it, the `ContextMenuStrip` reliably alternates
   open/no-open on consecutive right-clicks (13 consecutive shows in the cited log alternate
   almost perfectly `Visible=false`/`Visible=true`). KB135788's documented mechanism: "if the
   window is already in the foreground, the menu appears and immediately disappears on the
   second display" — and `PostMessage(WM_NULL)` "forces a task switch... preventing the menu
   from immediately disappearing on subsequent displays." This exact fix had been implemented
   once before and removed by a reader who assumed `SetForegroundWindow` + `menu.Show` were the
   complete fix (see the corrected `MenuAnchor.AtControl` doc comment and
   `TrayApp.ShowContextMenuAt`'s inline KB135788 comment — do not remove that `PostMessage`
   call again). Source:
   https://learn.microsoft.com/en-us/answers/questions/1125620/resolved-maui-trayicon-with-contextmenustrip-not-c

2. **Step 5's claim that "the foreground window at the moment ShowContextMenuAt captures it...
   is still the user's real prior window" is only sometimes true.** Live logs show
   `CaptureForegroundForRestore` rejecting essentially every candidate as `ownProcess=true` —
   meaning some other imrdy-owned window (a dashboard, a previous menu's transient popup) has
   usually already stolen foreground by click time, well before any AtControl grant happens.
   Capturing only at menu-show time is therefore too late in the common case. The fix: sample
   `GetForegroundWindow()` continuously — piggybacked on the existing 100ms drain timer
   (`TrayApp.OnDrainTimerTick` → `SampleForegroundForRestoreTracking`) rather than a new timer —
   and keep `_lastGoodForegroundWindow` updated whenever a legitimate (non-own-process,
   captioned) window is observed. `CaptureForegroundForRestore`'s own click-time check is
   retained as the freshest possible signal for the less-common case where the click itself is
   the first legitimate foreground change since the last tick, but the continuously-sampled
   value is what actually powers restoration in the common case.

**Discovered:** Step 6, diagnosing why the overlay context menu still opened only ~50% of the
time and why focus was never restored after step 5's fix landed.
**Impact:** Any future change to `ShowContextMenuAt`/`MenuAnchor.AtControl` must preserve both
the `PostMessage(WM_NULL)` call and the continuous foreground sampler — removing either
reintroduces one of the two original symptoms (menu not appearing / focus not restored).
`CLAUDE.md`'s "Overlay context menus" section's claim that no "SetForegroundWindow/WM_NULL/
ForceTopMost band-aids" are needed is also now stale and should be corrected at the next doc
sweep.

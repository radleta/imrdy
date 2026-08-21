---
tags: [imrdy-expert/overlay]
summary: "WM_MOUSEACTIVATE completes the foreground switch before the triggering button-down is delivered, so OnMouseDown/OnMouseUp run after activation — the reason a self-activating window cannot observe the user's prior foreground window"
---

## WM_MOUSEACTIVATE Completes Activation Before the Triggering Button-Down Is Delivered

> **Resolved differently — read this first.** The "Practical consequence" and "Impact" notes
> below were written mid-investigation, when `OverlayPanel.WndProc` returned `MA_ACTIVATE` for
> right-click. Step 5 reverted that: `WndProc` now returns `MA_NOACTIVATE` unconditionally, so
> the overlay never activates itself and the pre-click foreground window is still observable at
> menu-show time. The capture-inside-`WM_MOUSEACTIVATE` remedy proposed below was therefore
> never implemented and is **not** how the code works. See
> [overlay-context-menu-foreground-dance.md](../overlay-context-menu-foreground-dance.md) for
> the mechanism actually in use. The Win32 message-ordering fact itself is unaffected and is
> why that page's approach was needed.


When a window's `WndProc` returns `MA_ACTIVATE` from `WM_MOUSEACTIVATE`, Windows performs
the actual foreground-window switch as part of handling that message — synchronously,
before the mouse-button-down message (e.g. `WM_RBUTTONDOWN`) that triggered activation is
ever delivered to the window. `OnMouseDown`/`OnMouseUp` (WinForms events fired from those
button messages) therefore run *after* the activation has already happened.

Practical consequence for `OverlayPanel`'s new right-click `MA_ACTIVATE` policy
(step 3 of this fix): by the time `OverlayPanel.OnMouseUp` calls `_router.OpenSessionMenu`
→ `TrayApp.ShowContextMenuAt`, and that method calls `PInvokeWindow.GetForegroundWindow()`
right before `menu.Show(...)`, the foreground window is already the overlay itself — the
window the user was previously focused on (typically their terminal) is no longer
observable via `GetForegroundWindow()` at that point. Capturing "the window to restore
focus to" from inside `ShowContextMenuAt` therefore captures the overlay's own handle in
the common case, not the terminal. The only point in this flow where the pre-activation
foreground window is still observable is inside `OverlayPanel.WndProc`'s
`WM_MOUSEACTIVATE` handler itself, before it returns `MA_ACTIVATE`.

**Discovered:** During step 3 implementation of the overlay context-menu dismiss fix,
while implementing `TrayApp.ShowContextMenuAt`'s foreground-capture/restore pair per the
step spec (capture before `menu.Show`, restore on `Closed`). The step spec's own guard
list ("skip restore if the captured handle is the overlay's own handle") already
anticipates this outcome — flagging it here so the next iteration doesn't have to
rediscover the mechanism from scratch if the live test shows the restore is a no-op for
overlay-sourced right-clicks.

**Impact:** If a future live test shows `RestorePendingForeground` consistently hits its
own-handle guard for overlay right-clicks (i.e., terminal focus does not return after the
menu closes), the fix is to capture `GetForegroundWindow()` inside
`OverlayPanel.WndProc`'s `WM_MOUSEACTIVATE` handler — before returning `MA_ACTIVATE` — and
carry that captured handle forward (e.g. via a field or event) to
`TrayApp.ShowContextMenuAt`, rather than re-capturing it later from `ShowContextMenuAt`
itself.

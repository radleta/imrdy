---
tags: [imrdy-expert/desktop-routing]
summary: "SwitchToSessionDesktop 3-step routing: resolve target → switch desktop → guarded focus. WT skipped from dynamic lookup; ForceForeground guarded against ping-pong; auto-lock on SessionStart only"
---

# WT Desktop Routing

Clicking a tray dot for a session must (a) land the user on the right virtual desktop and (b) focus the terminal window — without dragging Windows back to a different desktop afterwards. `SwitchToSessionDesktop(SessionEntry entry)` in `src/Imrdy.Windows/TrayApp.cs` implements this in three deliberate stages:

## The three-step structure

| Step | Code region | Purpose |
|---|---|---|
| 1. Resolve target | first block of method | Pick the desktop the user *should* land on |
| 2. Switch desktop | second block | `_desktopManager.SwitchToDesktop(target.Value)` — fire this first, unconditionally |
| 3. Guarded focus | third block | Best-effort `ForceForeground(hwnd)`, suppressed when it would cause ping-pong |

Order matters: the desktop switch fires before any focus attempt because `ForceForeground` is the operation Windows can refuse from a balloon-tip context, while `SwitchToDesktop` works regardless. If focus fails, the user still lands on the right desktop.

## Target resolution: pinned > dynamic > unset

In priority order:

- **Pinned (`entry.DesktopIndex` is set)** — wins outright; `targetSource = "pinned"`. The user (or WT auto-lock on `SessionStart`) explicitly chose this desktop, so any later "where does the window actually live" answer is ignored.
- **Dynamic lookup (`entry.DesktopIndex is null` AND terminal is non-WT)** — call `_desktopManager.GetDesktopForWindow(hwnd)` on the terminal's main window handle; `targetSource = "dynamic"`. This handles the unpinned conhost / wezterm / non-WT case where the user drags the terminal window between desktops.
- **Unset** — when both fail (no pin, no hwnd, or hwnd is a WT window), `target` stays null. Step 2 is skipped — no desktop switch. Step 3 still fires unconditionally (preserves the original best-effort focus behavior for the dropped-on-the-floor case).

## WT is skipped from dynamic lookup

Windows Terminal v1.23+ runs **one process and one main HWND for all tabs and windows** (PR #14843 consolidated the prior Monarch/Peasant model). The HWND lives on whatever desktop WT was first opened on — there is no per-tab HWND or per-tab desktop. So `GetDesktopForWindow(WT-hwnd)` answers the same desktop for every WT session, regardless of which tab the click was for. Using it as the target would route every WT session to the same wrong place.

The dynamic-lookup branch therefore checks `IsWindowsTerminal(entry)` and bails out for WT. The pin remains the only correct source of truth for WT routing. For background on the WT process model, see the `windows-terminal-expert` skill's `process-model.md`.

## The compare-desktops focus guard

Even with target resolved correctly, calling `PInvokeWindow.ForceForeground(hwnd)` can cause ping-pong: the desktop switch lands the user on `target`, then ForceForeground brings WT's HWND (which lives on a different desktop) to the foreground, and Windows pulls the user back to that other desktop.

The guard, inside the `if (hwnd != IntPtr.Zero)` block:

```csharp
bool shouldFocus = true;
if (target.HasValue)
{
    var hwndDesktop = _desktopManager.GetDesktopForWindow(hwnd);
    if (hwndDesktop.HasValue && hwndDesktop.Value != target.Value)
    {
        _logger.LogInformation(
            "Focus: suppressed ForceForeground to avoid ping-pong — hwnd={Hwnd} hwndDesktop={HwndDesktop} target={Target}",
            hwnd, hwndDesktop.Value, target.Value);
        shouldFocus = false;
    }
    else if (!hwndDesktop.HasValue)
    {
        // COM unavailable or window gone — fail-safe skip
        shouldFocus = false;
    }
}
if (shouldFocus) { PInvokeWindow.ForceForeground(hwnd); }
```

Three cases:

- **`target` null** — no switch happened, no risk of pulling the user away. `ForceForeground` fires unconditionally (old behavior).
- **`hwndDesktop == target`** — terminal lives where the user just landed. `ForceForeground` fires. This is the lucky-match case for both pinned-and-actually-there and dynamic-lookup-set-target-from-hwnd.
- **`hwndDesktop != target`** — terminal lives elsewhere. Suppress `ForceForeground`; the user stays on `target` without focus. Log the suppression with hwnd, hwndDesktop, target so the diagnostic is visible in `imrdy_*.log`.
- **`hwndDesktop is null`** — COM unavailable or hwnd gone between resolution and lookup. Skip (fail-safe — the desktop switch already landed the user correctly).

The `target` is the desktop just switched to in step 2, so comparing `hwndDesktop` to `target` is functionally equivalent to comparing to `GetCurrentDesktopIndex()` but cheaper (no extra COM round-trip).

## WT auto-lock on SessionStart

A WT session with no pin has no usable target — dynamic lookup is skipped, so the user click does nothing useful. To fix this prospectively, `HandleSessionFileChanged`'s new-session branch auto-locks WT sessions to the user's currently active desktop on first observation, but only when the hook event is `SessionStart`:

```csharp
if (string.Equals(state.HookEvent, "SessionStart", StringComparison.OrdinalIgnoreCase)
    && entry.DesktopIndex is null
    && IsWindowsTerminal(entry))
{
    var currentDesktop = _desktopManager.GetCurrentDesktopIndex();
    if (currentDesktop.HasValue)
    {
        entry.DesktopIndex = currentDesktop.Value;
        PersistSessionDesktopIndex(entry);
    }
}
```

Why `SessionStart` only:

- `SessionStart` is the one hook event that fires at the actual moment of session launch. The user is by definition on the terminal's desktop when that event fires, so "the user's active desktop right now" is the correct binding.
- Other events (`PreToolUse`, `PostToolUse`, etc.) can fire while the user is on any desktop — auto-locking from them would bind to wherever the user happens to be sitting, not where the session lives.
- The tray-restart-mid-session case is explicitly excluded: when the tray restarts and re-bootstraps existing sessions, `state.HookEvent` is the last-written event, which may or may not be `SessionStart`. The guard limits auto-lock to genuine launches; restarts fall through to "no auto-lock" and rely on prior persistence.

The auto-lock writes through `PersistSessionDesktopIndex`, which means the `DesktopIndex` field must (and does) appear in the `FieldPreservation` list. See [Field Preservation Catalog](field-preservation-catalog.md).

## Residual race-loss hazard

The auto-lock write inherits the structural tray-vs-hook race documented in [Tray vs Hook Write Race](tray-hook-write-race.md). A concurrent hook event that read the state file before the auto-lock write lands can apply `PreserveFields` against a stale `existing` snapshot and clobber the freshly written `DesktopIndex` with `null`. The mitigation (`PreserveFields` listing) only protects against the post-write hook event, not the in-flight one. This is acknowledged technical debt — closing the race fully requires architectural change (one-writer model or write-coordination). For day-to-day operation, the window is small (~50-200ms hook RMW) and tray writes are infrequent, so loss is rare.

## Cross-references

- [Field Preservation Catalog](field-preservation-catalog.md) — `DesktopIndex` is sticky; auto-lock and menu actions both depend on this contract
- [Tray Persistence Verbs](tray-persistence-verbs.md) — `PersistSessionDesktopIndex` row
- [Tray vs Hook Write Race](tray-hook-write-race.md) — the structural race the auto-lock write inherits
- [Architecture](architecture.md) — overview

External: `windows-terminal-expert/process-model.md` for the WT v1.23+ single-process model rationale.

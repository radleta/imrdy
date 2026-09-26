---
tags: [imrdy-expert/dashboard]
summary: "WorkspaceDashboardForm + WorkspaceHoverDashboardController: BuildViewModel hit-index flow, VM-as-render-contract, live 'ago' refresh, Update-refresh-all-fields pattern, GitInfo Ahead/Behind, cross-controller hide via FormShown"
last-verified: "2026-09-25"
---

# Workspace Dashboard Architecture

## Overview

The workspace hover dashboard surfaces workspace identity in a form that appears on a 200ms dwell over a workspace dot in the overlay. The view model (`WorkspaceDashboardViewModel`) carries:

| Field | Source |
|---|---|
| Name | `WorkspaceEntry.Name` |
| WorkspacePath | `WorkspaceEntry.Path` |
| Desktop | `WorkspaceEntry.Desktop` (int index) |
| IsCurrentDesktop | `currentDesktopIndex == entry.Desktop` |
| IconStyle | `WorkspaceEntry.IconStyle` (null → the chip is hidden) |
| ActivityText | Precomputed "active Xh Ym ago" or "never seen" (builder, not form) |
| Git | `GitInfo` from the cache, or null; `Ahead` / `Behind` default to 0 |

## BuildViewModel Hit-Index Flow

`WorkspaceHoverDashboardController.BuildViewModel(item)` is called by the base controller after `TryHitTestForOurDomain` succeeds:

1. Extract `workspacePath = item.Id` from the resolved `DisplayItem` (workspace items carry path as `Id`)
2. `_workspaceStore.Load()` → `WorkspaceEntry? entry` (per-build call — no cache, YAGNI)
3. `_gitCache.TryGet(workspacePath)` → `GitInfo? cachedGit`
4. `_getCurrentDesktopIndex()` → `int? currentDesktopIndex` (delegate injected by TrayApp)
5. `_getWorkspaceLastSeenAt(workspacePath)` → `DateTimeOffset? lastSeenAt` (delegate injected by TrayApp)
6. `WorkspaceDashboardViewModelBuilder.Build(entry, cachedGit, currentDesktopIndex, lastSeenAt, DateTimeOffset.UtcNow)`

If `entry` is null (workspace was removed after the overlay snapshot was built), `BuildViewModel` returns `null` — the base controller treats null as "suppress show" (P7).

## Shared Shell — D2

`WorkspaceDashboardForm` derives from `HoverDashboardFormBase`, which owns the form chrome (FormBorderStyle.None, TopMost, rounded Region — no mica, focus guard, Pin/Unpin, Escape, anchor placement, all-desktops pinning; see [Hover Dashboard Form Lifecycle](hover-dashboard-form-lifecycle.md)). `WorkspaceDashboardForm` provides only the content panel: header row (Name + Desktop chip + Path + IconStyle chip), activity row, conditional git row, footer.

## Git Row

`GitInfo(Branch, DirtyCount, Ahead = 0, Behind = 0)` — `Ahead`/`Behind` are optional trailing parameters, so callers that don't need them construct it unchanged. The git row shows a branch chip and a dirty-count chip whenever `Git` is non-null, plus `↑N` / `↓N` chips only when the count is above zero. When `Git` is null the row collapses via `SetRowVisible`.

## VM-as-Complete-Render-Contract

`WorkspaceDashboardViewModelBuilder.Build` takes an explicit `DateTimeOffset now` parameter — pure function, deterministic. `ActivityText` is precomputed there; `WorkspaceDashboardForm` has **zero clock reads** and is a pure renderer of the VM snapshot. If the form read `DateTimeOffset.UtcNow` directly, two renders of the same fixture at different wall-clock times would differ and the visual seal would break. See [VM-as-Complete-Render-Contract](vm-as-complete-render-contract.md).

## Live "Ago" Refresh

`WorkspaceHoverDashboardController` overrides `OnSameItemRefreshTick(currentItem)` to call `BuildViewModel(currentItem)` again — with a fresh `UtcNow` — and hand the result to `UpdateCurrentForm`. The base calls it every `RefreshIntervalTicks=10` ticks (~1s at 100ms drain cadence), so the displayed "ago" string advances in real time while the form itself still reads no clock.

## Update Refreshes Every Field

Every control whose text, visibility or colors change per VM is a class field (`_nameLabel`, `_desktopChip`, `_pathLabel`, `_iconStyleChip`, `_activityLabel`, `_gitRow`), and `WorkspaceDashboardForm.Update(vm)` reassigns all of them in one pass: name, `Desktop {n}` chip, icon-style chip visibility and text (the path label narrows when the chip is shown), path, activity text, git-row visibility, and the rebuilt git chips.

That completeness is what makes workspace→workspace switch detection work: when the base sees the cursor move from workspace A to workspace B while the form is visible, it calls `ApplyViewModelUpdate(form, newVm)` → `form.Update(newVm)` on the existing form. A dynamic control left as a local inside `BuildLayout` is unreachable from `Update`, and the dashboard then shows B's activity under A's name and path — which is the bug that promoting every dynamic control to a field fixed. See [WinForms Update Field-Promote](winforms-update-field-promote.md).

## Cross-Controller Hide Protocol

The base raises `FormShown` after `TryShowForm` completes, and TrayApp cross-subscribes each controller's `HideIfVisible` to the other's `FormShown`, so hovering from a session icon to a workspace icon (or back) always shows exactly one dashboard. See [Hover Dashboard State Machine — Cross-Controller Hide Protocol](hover-dashboard-state-machine.md#cross-controller-hide-protocol).

## Related

- [Hover Dashboard Form Lifecycle](hover-dashboard-form-lifecycle.md) — Base/derived form split, field-promote pattern, BuildLayout/Update split
- [Hover Dashboard State Machine](hover-dashboard-state-machine.md) — Base controller dispatch chain, switch detection, FormShown protocol
- [VM-as-Complete-Render-Contract](vm-as-complete-render-contract.md) — Why builders take `now` and forms must not read the clock
- [WinForms Update Field-Promote](winforms-update-field-promote.md) — Field-promote rule, SetRowVisible, chip list rebuild
- [TableLayoutPanel Row Toggle](tablelayoutpanel-row-toggle.md) — Height-based row show/hide in TableLayoutPanel

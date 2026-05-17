---
tags: [imrdy-expert/dashboard]
summary: "WorkspaceDashboardForm + WorkspaceHoverDashboardController: BuildViewModel hit-index flow, VM-as-render-contract, live 'ago' refresh, Update-refresh-all-fields pattern, GitInfo Ahead/Behind, cross-controller hide via FormShown"
---

# Workspace Dashboard Architecture

## Overview

The workspace hover dashboard surfaces workspace identity in a non-layered WinForms form that appears on 200ms dwell over a workspace dot in the overlay. Fields shown:

| Field | Source |
|---|---|
| Name | `WorkspaceEntry.Name` |
| Path | `WorkspaceEntry.Path` |
| Desktop | `WorkspaceEntry.Desktop` (int index) |
| IsCurrentDesktop | `currentDesktopIndex == entry.Desktop` |
| IconStyle | `WorkspaceEntry.IconStyle` |
| ActivityText | Precomputed "active Xh Ym ago" or "never seen" (builder, not form) |
| Git Ahead / Behind | `GitInfo.Ahead`, `GitInfo.Behind` (additive `GitInfo` fields, D8) |

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

`WorkspaceDashboardForm` derives from `HoverDashboardFormBase` which owns the form chrome (FormBorderStyle.None, TopMost, DWM mica backdrop, rounded Region, focus guard, Pin/Unpin, Escape, anchor placement). `WorkspaceDashboardForm` provides only the content panel: header row (Name + Desktop chip + Path + IconStyle chip), activity row, conditional git row, footer.

## D8 GitInfo Extension

`GitInfo` record gained `Ahead` and `Behind` int fields (additive change — all callers that don't need them pass the same constructor args as before; default 0 is safe). The workspace dashboard renders "↑3 ↓1" ahead/behind indicators in the git row when `Git` is non-null.

## VM-as-Complete-Render-Contract

`WorkspaceDashboardViewModelBuilder.Build` takes an explicit `DateTimeOffset now` parameter — pure function, deterministic. VM carries:

- `ActivityText` — precomputed "active Xh Ym ago" or "never seen"; no clock reads inside the form
- All other fields — direct snapshots of `WorkspaceEntry` state at build time

`WorkspaceDashboardForm` has **zero clock reads**. The form is a pure renderer of the VM snapshot.

**Why**: if the form reads `DateTimeOffset.UtcNow` directly, two renders of the same fixture at different wall-clock times produce different output — the visual seal breaks. See [VM-as-Complete-Render-Contract](vm-as-complete-render-contract.md).

## Live "Ago" Refresh

`WorkspaceHoverDashboardController` overrides `OnSameItemRefreshTick(currentItem)`:

```csharp
protected override void OnSameItemRefreshTick(DisplayItem currentItem)
{
    // Rebuild VM with fresh UtcNow so ActivityText advances each ~1s
    var vm = (WorkspaceDashboardViewModel?)BuildViewModel(currentItem);
    if (vm is null) return;
    ApplyViewModelUpdate((HoverDashboardFormBase)_form!, vm);
}
```

The base calls `OnSameItemRefreshTick` every `RefreshIntervalTicks=10` ticks (~1s at 100ms drain cadence). The form's `Update(vm)` then reassigns `_activityLabel.Text = vm.ActivityText` along with all other dynamic fields — so the displayed "ago" string advances in real time.

## Update-Refresh-All-Fields

Every control whose text/visibility/colors change per-VM is declared as a class field:

```csharp
// ---- Layout child controls (field-promoted for Update() access) ----
private readonly Label _nameLabel;
private readonly Label _desktopChip;
private readonly Label _pathLabel;
private readonly Label _iconStyleChip;
private readonly Label _activityLabel;
private readonly FlowLayoutPanel _gitRow;
```

`WorkspaceDashboardForm.Update(vm)` reassigns all of them:

```csharp
public void Update(WorkspaceDashboardViewModel vm)
{
    _vm = vm;
    _nameLabel.Text     = vm.Name;
    _desktopChip.Text   = $"Desktop {vm.Desktop + 1}";
    _pathLabel.Text     = vm.WorkspacePath;
    _iconStyleChip.Text = vm.IconStyle ?? "circles";
    _activityLabel.Text = vm.ActivityText;

    // Git row: show/hide via SetRowVisible
    var hasGit = vm.Git is not null;
    SetRowVisible(RowGit, hasGit, GitRowHeight);
    if (hasGit) RebuildGitChips(vm.Git!);

    // Desktop chip highlight
    _desktopChip.BackColor = vm.IsCurrentDesktop ? Color.FromArgb(40, 120, 200) : Color.FromArgb(50, 52, 65);
}
```

`SetRowVisible(rowIndex, visible, height)` toggles `_tableLayout.RowStyles[rowIndex].Height` between 0 and `height` — same pattern as `SessionDashboardForm`. See [TableLayoutPanel Row Toggle](tablelayoutpanel-row-toggle.md).

**Workspace→workspace switch-detection**: when the base's Path B detects the cursor moved from workspace A to workspace B while the form is visible, it calls `ApplyViewModelUpdate(form, newVm)` → `form.Update(newVm)`. Because `Update` refreshes **all** dynamic fields, workspace B's data is correctly shown without re-creating the form.

**Discovery**: iter-5 revealed that only `_activityLabel` had been field-promoted after the iter-3/4 VM-as-contract fix. Other fields (`_nameLabel`, `_pathLabel`, `_desktopChip`, `_iconStyleChip`) were still local variables unreachable from `Update`. Workspace→workspace switching therefore showed stale name/path/desktop. Promoting all dynamic controls to class fields fixed the bug and made `Update` the complete content source.

## Cross-Controller Hide Protocol

`WorkspaceHoverDashboardController.FormShown` is raised by the base after `TryShowForm` completes. TrayApp subscribes:

```csharp
_workspaceController.FormShown += () => _sessionController.HideIfVisible();
_sessionController.FormShown   += () => _workspaceController.HideIfVisible();
```

This ensures that hovering from a session icon to a workspace icon (or vice versa) always shows exactly one dashboard. See [Hover Dashboard State Machine — Cross-Controller Hide Protocol](hover-dashboard-state-machine.md).

## Related

- [Hover Dashboard Form Lifecycle](hover-dashboard-form-lifecycle.md) — Base/derived form split, field-promote pattern, BuildLayout/Update split
- [Hover Dashboard State Machine](hover-dashboard-state-machine.md) — Base controller dispatch chain, FormShown protocol, state diagram
- [VM-as-Complete-Render-Contract](vm-as-complete-render-contract.md) — Why builders take `now` and forms must not read the clock
- [WinForms Update Field-Promote](winforms-update-field-promote.md) — Field-promote rule, SetRowVisible, chip list rebuild
- [TableLayoutPanel Row Toggle](tablelayoutpanel-row-toggle.md) — Height-based row show/hide in TableLayoutPanel

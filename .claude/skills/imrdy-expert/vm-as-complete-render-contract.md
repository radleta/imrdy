---
tags: [imrdy-expert/dashboard]
summary: "VM-as-complete-render-contract: builders take an explicit 'now' parameter and precompute every display string; forms/renderers have zero clock reads, so a fixture renders the same PNG at any hour"
last-verified: "2026-09-25"
---

# VM-as-Complete-Render-Contract

## Principle

The view model is the **complete snapshot** the renderer reads. No field is derived inside the renderer.

**Forbidden in any form, renderer, or UserControl:**
- `DateTimeOffset.UtcNow` / `DateTime.Now` reads for display strings
- Runtime queries (store lookups, cache reads, desk-manager calls)
- Any state derivation that re-computes what the builder already computed

**Required in the builder:**
- Accept an explicit `now` parameter (or equivalent "context snapshot")
- Compute all time-derived strings (e.g., `ActivityText`) before returning
- Return a record that is a complete, immutable snapshot

## Pattern

```csharp
// Builder (Core layer, pure function)
public static WorkspaceDashboardViewModel Build(
    WorkspaceEntry entry,
    GitInfo? cachedGit,
    int? currentDesktopIndex,
    DateTimeOffset? lastSeenAt,
    DateTimeOffset now)   // <-- explicit now: deterministic, testable
{
    var activityText = lastSeenAt is null
        ? "never seen"
        : $"active {RelativeTimeFormatter.FormatDuration(now - lastSeenAt.Value)} ago";

    return new WorkspaceDashboardViewModel(
        WorkspacePath: entry.Path,
        Name: entry.Name,
        ...
        ActivityText: activityText,   // <-- precomputed string
        Git: cachedGit);
}

// Form (Windows layer, zero clock reads)
public void Update(WorkspaceDashboardViewModel vm)
{
    _activityLabel.Text = vm.ActivityText;  // <-- just display what builder decided
    // NO: _activityLabel.Text = $"active {(DateTimeOffset.UtcNow - vm.LastSeenAt.Value)}...";
}
```

## Why

**Determinism for visual seal tests**: `imrdy render --all` captures PNGs of WinForms surfaces. If the form reads `UtcNow` inside `Update`, two renders of the same fixture captured hours apart produce different pixels — the visual seal regression check breaks or, worse, silently hides regressions behind noise.

**Zero live-stale**: `Update(vm)` refreshes every dynamic field from the VM in one pass. There is no field that advances on its own or drifts from the VM's snapshot.

**Single source of truth**: the builder owns all computation; the renderer owns zero computation. A test can verify builder output independently of WinForms.

## Live "Ago" Advancement

The VM-as-contract pattern applies to the _content_ of the VM. Keeping "ago" strings advancing on a live dashboard is handled by the controller calling `BuildViewModel` with fresh `UtcNow` every ~1s (via `OnSameItemRefreshTick`) and passing the new VM to `Update`. The form itself still has zero clock reads — it just receives a freshly-built VM each tick.

## Counter-Example: SparklineControl

`SparklineControl` has a `ReferenceTime` property (defaults to `DateTimeOffset.UtcNow` when unset). This is a hybrid: the sparkline receives many timestamped data points that must be re-anchored relative to "now" for the X-axis. For display strings like "active X ago", the cleaner pattern is precompute-in-builder. The two patterns co-exist; the distinction is:

- **Static display strings** (labels, chips) → precompute in builder (this pattern)
- **Multi-point time-series rendering** (sparklines) → `ReferenceTime` anchor in the control is acceptable

See [Sparkline Reference Time](sparkline-reference-time.md) for the `ReferenceTime` pattern.

## How a clock leak shows up

A form that reads the clock passes its first visual seal and fails a later one: the workspace dashboard's fixture rendered "active 5h 40m ago" at baseline and "active 6h 11m ago" hours later, because `WorkspaceDashboardForm.Update` read `DateTimeOffset.UtcNow`. Moving the computation into `WorkspaceDashboardViewModelBuilder.Build(entry, git, desktopIndex, lastSeenAt, now)` fixed it. A single seal run cannot catch this class — re-render an unchanged fixture later and diff, or grep the form for clock reads.

## Related

- [Workspace Dashboard Architecture](workspace-dashboard-architecture.md) — Full workspace dashboard context
- [WinForms Update Field-Promote](winforms-update-field-promote.md) — Field-promote rule that makes Update the sole content source
- [Sparkline Reference Time](sparkline-reference-time.md) — Counter-example: hybrid ReferenceTime anchor for multi-point rendering

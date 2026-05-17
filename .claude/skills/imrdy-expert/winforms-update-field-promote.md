---
tags: [imrdy-expert/dashboard]
summary: "Field-promote all dynamic WinForms controls for Update(vm) access; BuildLayout/Update split; SetRowVisible for conditional rows; chip list clear+rebuild — workspace→workspace stale-fields bug was caused by missing field promotion"
---

# WinForms Update — Field-Promote Pattern

## Principle

Any WinForms control whose text, visibility, back-color, or fore-color changes per-VM must be declared as a **class field**, not a local variable inside a helper method.

**Why:** `Update(vm)` is the sole content path — it runs on every VM refresh. Local variables created in `BuildLayout()` or other helper methods are unreachable from `Update`. If a dynamic control is a local, the only way to update it is re-creating the entire layout — which is slow and unnecessary.

## Pattern

```csharp
internal sealed class WorkspaceDashboardForm : HoverDashboardFormBase
{
    // ---- Field-promoted dynamic controls ----
    private readonly Label _nameLabel;
    private readonly Label _desktopChip;
    private readonly Label _pathLabel;
    private readonly Label _iconStyleChip;
    private readonly Label _activityLabel;
    private readonly FlowLayoutPanel _gitRow;

    // ---- Static-layout helpers (NOT field-promoted) ----
    private TableLayoutPanel _tableLayout = null!;  // assigned in BuildLayout

    public WorkspaceDashboardForm(WorkspaceDashboardViewModel vm, ...)
    {
        // 1. Create controls as fields (VM-agnostic)
        _nameLabel     = new Label { ... };
        _desktopChip   = new Label { ... };
        _pathLabel     = new Label { ... };
        _iconStyleChip = new Label { ... };
        _activityLabel = new Label { ... };
        _gitRow        = new FlowLayoutPanel { ... };

        // 2. Build layout skeleton (VM-agnostic)
        BuildLayout();

        // 3. Populate content from VM
        Update(vm);
    }

    private void BuildLayout()
    {
        // Add controls to panels, set fonts/padding, wire layout structure.
        // No VM-specific values here.
        _tableLayout = new TableLayoutPanel { ... };
        _tableLayout.Controls.Add(_nameLabel,   0, RowHeader);
        _tableLayout.Controls.Add(_activityLabel, 0, RowActivity);
        _tableLayout.Controls.Add(_gitRow,       0, RowGit);
        Controls.Add(_tableLayout);
    }

    public void Update(WorkspaceDashboardViewModel vm)
    {
        // Sole content source — reassigns all dynamic controls
        _nameLabel.Text     = vm.Name;
        _desktopChip.Text   = $"Desktop {vm.Desktop + 1}";
        _pathLabel.Text     = vm.WorkspacePath;
        _iconStyleChip.Text = vm.IconStyle ?? "circles";
        _activityLabel.Text = vm.ActivityText;

        var hasGit = vm.Git is not null;
        SetRowVisible(RowGit, hasGit, GitRowHeight);
        if (hasGit) RebuildGitChips(vm.Git!);
    }
}
```

## Conditional Rows

Use `SetRowVisible(rowIndex, visible, height)` to toggle `TableLayoutPanel` row heights:

```csharp
private void SetRowVisible(int rowIndex, bool visible, int height)
{
    _tableLayout.RowStyles[rowIndex] = new RowStyle(
        SizeType.Absolute,
        visible ? height : 0);
}
```

Reference: `SessionDashboardForm.SetRowVisible` — same pattern. Height=0 collapses the row; height=N restores it. Wrap it in `SuspendLayout`/`ResumeLayout` if the toggle is performance-sensitive.

See [TableLayoutPanel Row Toggle](tablelayoutpanel-row-toggle.md) for MinimumSize / AutoSize interaction details.

## Chip Lists

Chip list controls (FlowLayoutPanels populated with per-item labels) must be **cleared and rebuilt** on every `Update`:

```csharp
private void RebuildGitChips(GitInfo git)
{
    // Dispose old controls before clearing (prevents GDI handle leak)
    foreach (Control c in _gitRow.Controls)
        c.Dispose();
    _gitRow.Controls.Clear();

    if (git.Ahead > 0) _gitRow.Controls.Add(MakeChip($"↑{git.Ahead}"));
    if (git.Behind > 0) _gitRow.Controls.Add(MakeChip($"↓{git.Behind}"));
    if (!string.IsNullOrEmpty(git.Branch)) _gitRow.Controls.Add(MakeChip(git.Branch));
}
```

Chip lists are NOT field-promoted (the chips themselves are transient). The container panel (`_gitRow`) IS field-promoted.

## BuildLayout / Update Split

The constructor follows this two-phase shape:

1. **Field creation** — instantiate controls as fields (no VM-specific values)
2. **`BuildLayout()`** — build the VM-agnostic layout skeleton (TableLayoutPanel structure, fonts, fixed colors, padding)
3. **`Update(vm)`** — assign all VM-specific values

`SessionDashboardForm` follows the same pattern. When `SessionHoverDashboardController` calls `form.Update(newVm)` on a workspace→workspace switch, `Update` refreshes every dynamic field cleanly.

## Discovery

**iter-5 bug**: after the iter-3/4 fix that introduced `ActivityText` as a builder-precomputed field and promoted `_activityLabel` to a class field, workspace→workspace switching still showed stale name/path/desktop for workspace B. Investigation revealed that `_nameLabel`, `_pathLabel`, `_desktopChip`, and `_iconStyleChip` were still local variables in `BuildLayout` — `Update(vm)` could not reach them. Promoting all four to class fields and adding assignments in `Update` fixed the bug.

**Lesson**: when adding a new dynamic field to a form, always check whether the corresponding control is a class field. If it's a local, field-promote it before wiring the `Update` assignment.

## Related

- [Workspace Dashboard Architecture](workspace-dashboard-architecture.md) — Full workspace dashboard context with this pattern applied
- [VM-as-Complete-Render-Contract](vm-as-complete-render-contract.md) — Why `Update(vm)` must be the sole content source
- [TableLayoutPanel Row Toggle](tablelayoutpanel-row-toggle.md) — Row height toggling details

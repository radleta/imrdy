---
tags: [imrdy-expert/dashboard]
summary: "Field-promote all dynamic WinForms controls for Update(vm) access; BuildLayout/Update split; SetRowVisible for conditional rows; chip list clear+rebuild — a dynamic control left as a local shows stale fields on switch"
last-verified: "2026-09-25"
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
        _nameLabel.Text        = vm.Name;
        _desktopChip.Text      = $"Desktop {vm.Desktop}";
        _iconStyleChip.Visible = vm.IconStyle is not null;
        if (vm.IconStyle is not null) _iconStyleChip.Text = vm.IconStyle;
        _pathLabel.Text        = vm.WorkspacePath;
        _activityLabel.Text    = vm.ActivityText;

        SetRowVisible(RowGit, vm.Git is not null, GitRowHeight);
        UpdateGitChips(vm.Git);   // disposes stale chips; adds new ones when Git is non-null
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
private void UpdateGitChips(GitInfo? git)
{
    // Dispose old controls before clearing (prevents GDI handle leak)
    foreach (Control c in _gitRow.Controls)
        c.Dispose();
    _gitRow.Controls.Clear();

    if (git is null) return;
    _gitRow.Controls.Add(MakeChip($"⎇ {git.Branch}", ImrdyPalette.FgSecondary));
    _gitRow.Controls.Add(MakeChip($"+{git.DirtyCount}", ImrdyPalette.FgSecondary));
    if (git.Ahead > 0)  _gitRow.Controls.Add(MakeChip($"↑{git.Ahead}", ImrdyPalette.FgSecondary));
    if (git.Behind > 0) _gitRow.Controls.Add(MakeChip($"↓{git.Behind}", ImrdyPalette.FgSecondary));
}
```

Chip lists are NOT field-promoted (the chips themselves are transient). The container panel (`_gitRow`) IS field-promoted.

## BuildLayout / Update Split

The constructor follows this two-phase shape:

1. **Field creation** — instantiate controls as fields (no VM-specific values)
2. **`BuildLayout()`** — build the VM-agnostic layout skeleton (TableLayoutPanel structure, fonts, fixed colors, padding)
3. **`Update(vm)`** — assign all VM-specific values

`SessionDashboardForm` follows the same pattern. When the hover controller calls `form.Update(newVm)` on a switch from one item to another, `Update` refreshes every dynamic field cleanly.

## Why it matters

A form whose `Update` reaches only some of its dynamic controls looks correct on first show and goes stale on the first switch: workspace→workspace traversal once showed workspace B's activity under workspace A's name, path and desktop, because those labels were locals inside `BuildLayout` and only `_activityLabel` had been promoted. When adding a dynamic field to a form, check that its control is a class field before wiring the `Update` assignment.

## Related

- [Workspace Dashboard Architecture](workspace-dashboard-architecture.md) — Full workspace dashboard context with this pattern applied
- [VM-as-Complete-Render-Contract](vm-as-complete-render-contract.md) — Why `Update(vm)` must be the sole content source
- [TableLayoutPanel Row Toggle](tablelayoutpanel-row-toggle.md) — Row height toggling details

---
tags: [imrdy-expert/winforms]
summary: "TableLayoutPanel row toggling via Absolute height 0; MinimumSize (not Width) pins fixed width with AutoSize=GrowAndShrink"
---

## TableLayoutPanel Row Toggle: Use Absolute Height 0 — Not Control.Visible or Dock=Fill

The canonical WinForms pattern for conditionally collapsing a `TableLayoutPanel` row is to
**toggle the row's `RowStyle` height between the desired pixel value and 0** (both as
`SizeType.Absolute`). Do NOT use `Control.Visible = false` on the cell's control — that
hides the control but leaves the row height allocated, causing blank gaps.

```csharp
private void SetRowVisible(int rowIndex, bool visible, int height)
{
    _tableLayout.RowStyles[rowIndex] = new RowStyle(SizeType.Absolute, visible ? height : 0f);
}
```

**Critical constraints:**
1. Store the desired pixel heights as constants — do NOT read them from `control.Height`
   after `Dock = DockStyle.Fill` is applied. Once `Dock=Fill`, `control.Height` reflects
   the current cell height (which may be 0 initially), not the intended content height.
2. Controls placed in table cells should use `Dock = DockStyle.Fill` — the row height
   drives the pixel budget; the control fills that budget.
3. Do NOT set `AutoSize=true` on the `TableLayoutPanel` if ANY fixed-height Absolute rows are
   present AND the panel has `Dock=Fill`. `Dock=Fill` and `AutoSize` conflict — WinForms
   zeroes the control. Instead: set `AutoSize=true` WITHOUT `Dock` on the panel, and let the
   form's own `AutoSize=true` size to match the panel.

**Form-level AutoSize pattern that avoids the Dock+AutoSize conflict:**
```csharp
// Form ctor — use MinimumSize to pin width, NOT Width or MaximumSize.
// Setting MaximumSize.Height = 0 collapses the form to zero height (documented MS gotcha).
MinimumSize = new Size(FormMinWidth, 0);
// MaximumSize stays Size.Empty — never set MaximumSize.Height = 0 explicitly.
AutoSize = true;
AutoSizeMode = AutoSizeMode.GrowAndShrink;

// In BuildLayout
_tableLayout = new TableLayoutPanel
{
    AutoSize     = true,
    AutoSizeMode = AutoSizeMode.GrowAndShrink,
    Left         = 0,
    Top          = 0,
    Width        = ClientSize.Width,  // captures FormMinWidth at ctor time (FormBorderStyle.None)
    // NO Dock = DockStyle.Fill
};
Controls.Add(_tableLayout);
```

The form auto-sizes to the panel's height sum; the panel's width is pinned to the form's
`ClientSize.Width` at construction time. Row height changes via `SetRowVisible` automatically
propagate to the form height because `AutoSize=true` on both panel and form.

**Critical: use `MinimumSize` not `Width` to pin the fixed width.** Setting `Width` directly is
overridden by AutoSize. `MaximumSize` must remain `Size.Empty` — `MaximumSize = new Size(w, 0)`
collapses the form height to zero because WinForms interprets height 0 as "no height allowed".

**Why the FlowLayoutPanel+Dock=Fill alternative failed (3 iters):**
A `FlowLayoutPanel` with `FlowDirection=TopDown` and `Dock=Fill` inside a scroll `Panel`
with `Dock=Fill` produces zero-height output in headless/`DrawToBitmap` paths because:
- `Dock=Fill` children are sized AFTER the parent computes its own size
- A `FlowLayoutPanel` with `FlowDirection=TopDown` computes its preferred size from its
  children's heights, but children with `Dock=Fill` are excluded from this computation
- Result: parent reports preferred height = 0, so layout assigns 0 height to the panel

`TableLayoutPanel` with `Absolute` row heights avoids this entirely — the layout engine
knows the height of every row before it asks children to size themselves.

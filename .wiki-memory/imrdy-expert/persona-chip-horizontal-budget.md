---
tags: [imrdy/dashboard-layout, imrdy/winforms-patterns]
updated: 2026-04-25
summary: "WinForms Anchor-based layouts: invisible-but-present sibling controls reduce available width for Anchor=Left|Right peers"
---

## WinForms Anchor-Based Layouts: Invisible Siblings Steal Width

In `DashboardForm`'s header layout, when a dormant control (e.g., `_personaChip`) is added to `Controls` with `Visible=true` and a non-zero `Width`, it reserves horizontal space even when its text is empty or placeholder text. This forces neighboring `Anchor=Left|Right` controls (like the session-name label) to shrink and triggers `AutoEllipsis` truncation.

**The gotcha:** Setting `Visible=false; Width=0` does not always release space cleanly under WinForms Anchor rules. The control still participates in layout calculations.

**The fix:** Remove dormant controls from `Controls` entirely via `Controls.Remove()` — do not add them in the first place. The field can remain declared for future re-introduction, but its layout footprint must be zero.

**Real-world case:** Session name `overlay-dashboard-context` (24 chars) truncated to `overlav-dashboard-context` (ellipsis) at form `MinimumSize = (520, 0)` — mockup-exact 520 px and far above the 360 px floor that originally caused truncation. Increasing form width did NOT fix this because the persona chip's fixed-width reservation scaled with it, keeping the session label shrunk. Two visual seal iterations were needed to surface the root cause (budget-stealing, not a label-width constraint).

**Generalization:** In Anchor-based layouts, any invisible-but-present sibling with non-zero `Width` reduces the available width for its `Anchor=Left|Right` peers. When introducing new chip-style controls (persona, cwd, desktop-chip, fleet-member, etc.), add this invariant to the header-layout checklist: all dormant chips must use `Controls.Remove` (or never add), not `Visible=false`.

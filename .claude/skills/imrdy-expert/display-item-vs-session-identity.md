---
tags: [imrdy-expert/display-model]
summary: "DisplayItem uses Id + ItemType and is the filtered, sorted tray/overlay snapshot; SessionEntry has SessionId and full session state — choose by whether visibility filtering matters"
last-verified: "2026-09-25"
code-cites:
  - src/Imrdy.Core/Display/DisplayItem.cs
  - src/Imrdy.Core/Display/DisplayItemCollection.cs
  - src/Imrdy.Windows/Dashboard/LiveDashboardVmBuilder.cs
---

## DisplayItem vs SessionEntry — Which Identity to Use

`DisplayItem` (`Imrdy.Core/Display/`) and `SessionEntry` (`Imrdy.Windows/Models/`) have different identity schemes.

| Field | DisplayItem | SessionEntry |
|-------|-------------|--------------|
| Identity | `Id` — session id for a session, workspace **path** for a workspace | `SessionId` |
| Type discriminator | `ItemType` (`Session` \| `Workspace`) | N/A (only sessions) |
| Label | `Label` (session or workspace name) | `State.SessionName` |
| Status | `Status` — already resolved for display | `State.Status` (lead readiness) and `EffectiveStatus` (display) |

| Goal | Source | Why |
|------|--------|-----|
| Session list with names and hover state (e.g. the dashboard fleet strip) | `IReadOnlyList<SessionEntry>` | Has `SessionId` and the full state directly; no mapping |
| Anything that must tell sessions from workspaces | `DisplayItem` with `ItemType` | The only model carrying both |
| Tray / overlay projection | `IReadOnlyList<DisplayItem>` | Already filtered for visibility (dismissed, `RemoveAfter`, ended) and sorted |

The fleet strip is the worked example: `LiveDashboardVmBuilder.ProjectFleetItems(IReadOnlyList<SessionEntry>, hoveredSessionId)` projects `SessionEntry` straight to `FleetItem`, avoiding a `DisplayItem.Id` → `SessionId` mapping and a second lookup for names.

**Use `DisplayItem`** for the tray and overlay render paths and any code that must respect workspace filtering or session visibility. **Use `SessionEntry`** for dashboard state, controller logic that needs full session context, and aggregations that ignore visibility.

## Two DisplayItem properties that are easy to misread

- **`IsVisible` is always true on a built item.** `DisplayItemCollection.Build` drops invisible inputs before mapping, so the flag carries no information after `Build`; the decision lives on `DisplayItemInput.IsVisible`.
- **List order is the overlay's left-to-right order.** `Build` sorts null `DesktopIndex` last, then by `DesktopIndex`, then sessions before workspaces on the same desktop — so a desktop's sessions and workspaces sit adjacent. Hit-testing (`DisplayItemCollection.TryGetItemAtClientPoint`) indexes into that same order, so anything that reorders the list after `Build` desynchronizes click targets from what is drawn.

`IsDisconnected` is a separate dimension from `AgingTier`, never a sixth tier: `AgingTier` owns opacity, `IsDisconnected` owns geometry (`DisconnectedGlyph`; what sets it is [Publisher Liveness](publisher-liveness.md)). It is always false for a local session and for a workspace.

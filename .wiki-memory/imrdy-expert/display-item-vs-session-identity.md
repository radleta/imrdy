---
tags: [imrdy-expert/display-model]
updated: 2026-04-26
summary: "DisplayItem uses Id + ItemType; SessionEntry has SessionId. Choose based on context."
---

## DisplayItem vs SessionEntry — Which Identity to Use

`DisplayItem` (in `Imrdy.Core.Display/`) and `SessionEntry` (in `Imrdy.Windows/Models/`) have different identity schemes.

| Field | DisplayItem | SessionEntry |
|-------|-------------|--------------|
| Identity | `Id` (string) | `SessionId` (string) |
| Type discriminator | `ItemType` (Session\|Workspace) | N/A (only sessions) |
| Session name | Not included | `State.SessionName` |
| Status | `Status` (string) | `State.Status` (string) |

**Decision tree for fleet/summary projections:**

| Goal | Source | Why |
|------|--------|-----|
| Build session list with names and hover state | `_sessionSource()` → `IReadOnlyList<SessionEntry>` | SessionEntry has SessionId + SessionName directly; no mapping needed |
| Filter workspaces from display items | Use `DisplayItem` with `ItemType == DisplayItemType.Workspace` | DisplayItem is the only source that distinguishes sessions from workspaces |
| Build display-layer projection (tray/overlay) | `_displayItemSource()` → `IReadOnlyList<DisplayItem>` | Represents tray/overlay filtered state (respects Dismissed, RemoveAfter, etc.) |

**Simpler fleet pattern — use SessionEntry:**

```csharp
private static IReadOnlyList<FleetItem> ProjectFleetItems(
    IReadOnlyList<SessionEntry> sessions, string hoveredSessionId)
{
    var fleet = new List<FleetItem>(sessions.Count);
    foreach (var s in sessions)
    {
        fleet.Add(new FleetItem(
            SessionId: s.SessionId,
            SessionName: s.State.SessionName ?? "",
            Status: s.State.Status,
            IsHovered: s.SessionId == hoveredSessionId));
    }
    return fleet;
}
```

Avoids mapping `DisplayItem.Id` → `SessionId` and querying session names from elsewhere.

**When to use DisplayItem:** Tray render path, overlay render path, any code that respects workspace filtering or session visibility state (Dismissed, RemoveAfter aging).

**When to use SessionEntry:** Dashboard state building, controller logic that needs full session context, any aggregation that ignores visibility state.

---

**Discovered:** Step 8 backend wiring — attempted fleet projection from DisplayItem before consulting SessionEntry definition. Both record types are valid; context determines which is simpler to work with.

**Impact:** Dashboard and controller code touching session identity should prefer SessionEntry when full state is available, and DisplayItem only when filtering/visibility is required.

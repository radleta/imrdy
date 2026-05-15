---
tags: [imrdy-expert/hooks]
summary: "Construct HookEventModel from StateFileModel when calling Apply from FSW path."
---

## Calling HookAccumulationStore.Apply from FileSystemWatcher Path

`HookAccumulationStore.Apply(HookEventModel evt, string derivedStatus)` expects a `HookEventModel`, but the FileSystemWatcher path in `TrayApp.HandleSessionFileChanged` reads a `StateFileModel` from disk — the original stdin JSON is not retained.

**Field mapping — StateFileModel → HookEventModel:**

```csharp
var evt = new HookEventModel
{
    HookEventName = state.HookEvent,        // StateFileModel.HookEvent
    SessionId = state.SessionId,             // StateFileModel.SessionId
    ToolName = state.ToolName,               // StateFileModel.ToolName (nullable)
    NotificationType = state.NotificationType, // StateFileModel.NotificationType (nullable)
    // AgentId is NOT available from StateFileModel — set to null (teammate detection not applicable from FSW path)
};
_hookAccumulationStore.Apply(evt, derivedStatus: entry.State.Status);
```

**Key differences:**

| StateFileModel | HookEventModel |
|---|---|
| `HookEvent` (string) | `HookEventName` (string) — same field, renamed |
| `Timestamp` (DateTimeOffset) | No Timestamp field — HookEventModel doesn't track timestamp; accumulator uses `DateTimeOffset.UtcNow` internally |
| `ToolName`, `NotificationType` | Same fields, nullable |
| No AgentId field | `AgentId` field exists in HookEventModel but is null from FSW path (only hook command stdin provides it) |

**Why this is required:** The FSW path doesn't have access to the original stdin JSON. The state file is the permanent record; reconstructing a minimal `HookEventModel` from its fields is sufficient to drive the accumulator (turn count, recent tools list, failure tracking).

**When to use:** Any code that needs to call `Apply` from a non-stdin source (FSW, UI timer drain, background job) must construct a `HookEventModel` from available state fields.

**When NOT to use:** `HookCommand` directly receives stdin JSON as `HookEventModel` — no reconstruction needed.

---

**Discovered:** Step 8 backend wiring — needed to call `Apply` from `HandleSessionFileChanged` FSW callback. Initial attempt tried to pass `StateFileModel` directly (compiler error). Solution: construct minimal `HookEventModel` from state fields.

**Impact:** Any future hook-driven accumulator or state pipeline that touches the FSW path must understand this mapping. Pattern is stable — applies to all StateFileModel → HookEventModel conversions in the codebase.

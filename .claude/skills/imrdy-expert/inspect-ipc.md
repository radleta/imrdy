---
tags: [imrdy-expert/ipc]
summary: "Tray IPC: render-live and inspect-live verbs — pipe protocol, dev-default gate, walker+analyzer, threading model, ACL"
---

# Tray IPC: render-live and inspect-live verbs

## Pipe name and protocol

Pipe name: `Local\ImrdyInspect` (constant `ImrdyPaths.InspectPipeName`).

Framing: 4-byte **little-endian length prefix** followed by a UTF-8 JSON body, used in both directions (request → server, response → client).

- **Request**: `InspectRequest(Verb, SessionId, OutputPath?)` — `OutputPath` is null for `inspect-live`, required absolute path for `render-live`.
- **Response**: `InspectResponse(SchemaVersion, Verb, Error?, Render?, Inspect?)` — exactly one of `Render` or `Inspect` is non-null on success; both are null on error.

All types are source-generated via `ImrdyJsonContext` (camelCase property naming policy). No reflection.

## Request/response framing (4-byte LE length prefix)

```
[4 bytes: body length, little-endian uint32]
[N bytes: UTF-8 JSON body]
```

Both client and server read the length prefix first, then allocate an exact-size buffer and read exactly N bytes. `BinaryPrimitives.ReadInt32LittleEndian` / `WriteInt32LittleEndian` are used (no endian ambiguity). Max request body: 4 KiB. Max response buffer: 256 KiB.

## Dev-default gate

`DiagnosticsConfig.IpcEnabled` (`bool?`) lives in `ImrdyConfig.Diagnostics`.

Resolution rule used by `TrayApp`: `IpcEnabled ?? File.Exists(ImrdyPaths.DevBuildMarker)`

- **null** (default, not in config.json): IPC starts if `~/.imrdy/.dev-build` exists — i.e., auto-on for dev builds, off in production.
- **true**: IPC always starts regardless of marker.
- **false**: IPC never starts.

`EnsureDefaults` does **not** flatten null to false — the three-state semantics are intentional. Callers MUST use the `?? File.Exists(...)` idiom, never assume `IpcEnabled == false` means disabled.

To enable in production: `imrdy config set diagnostics.ipcEnabled true` (or add `"diagnostics": { "ipcEnabled": true }` to `config.json`).

## Schema versioning policy

`schemaVersion` field is `"1"` (string literal, not integer).

- **Additive changes** (new optional fields, new `kind` values in `DiagnosticFinding`) are non-breaking within v1. Agents SHOULD tolerate unknown fields (forward-compatible reads).
- **Breaking changes** (field removal, type change, semantic change) bump to `"2"`. v1 and v2 will be co-served for at least one release cycle.
- Agents SHOULD check `schemaVersion` before processing; treat unknown versions as future schemas and degrade gracefully.

Full JSON shape documented in `docs/dashboard-inspect-schema.md`.

## Walker output shape (inspect-live)

`InspectService` (Windows-side, UI-thread-only) walks the live `DashboardForm`:

- **`form`** (`FormGeometry`): `formX`, `formY`, `formWidth`, `formHeight`, `clientWidth`, `clientHeight`, `regionRadius`. Field names are camelCase of the C# record param names — note `formX`/`formWidth` (not `x`/`width`).
- **`tree`** (`LayoutNode[]`): flat BFS pre-order list. Index 0 is always the form root. Each node carries `type`, `name`, `text` (truncated at 200 chars), bounds (`boundsX/Y/Width/Height`), colors, font, `anchor`, `dock`, `visible`, padding/margin per-edge, `childIndexes` (indexes into the flat list), and `details` (string→string — carries `"row[N]"` computed heights for `TableLayoutPanel` nodes).
- **`diagnostics`** (`DiagnosticFinding[]`): zero or more findings. Each has `kind`, `severity`, `controlPath`, `message`, `details`.
- **`diagnosticTimestamp`**: ISO 8601 UTC string of when analysis ran.

## Analyzer finding categories

`LayoutAnalyzer` (Core-side, pure/stateless) runs four detectors in order:

| Kind | Severity | Trigger | Details keys |
|---|---|---|---|
| `regionClipRisk` | `warning` (no text) / `error` (has text) | Control bounds intersect a rounded-corner box (radius = `form.regionRadius`) | `corner`, `clippedPixels` |
| `siblingOverlap` | `warning` | Two sibling controls share overlapping bounds (after allow-list; `accentBar`+`headerPanel` overlap is allowed) | `controlA`, `controlB`, `overlapArea` |
| `edgeProximity` | `info` | Control is within 4 px of the form client edge | `edges` (comma-separated: `left`, `top`, `right`, `bottom`) |
| `collapsedRow` | `info` | A `TableLayoutPanel` row height is `"0"` in the node's `details` map | `rowIndex` |

`controlPath` is a slash-separated path from form root to the affected control, e.g. `DashboardForm/TableLayoutPanel[mainLayout]/Panel[sparklinePanel]`. Each segment is `Type` or `Type[Name]` (name omitted when empty).

## Threading model

```
CLI process                   Named pipe                Tray UI thread
───────────────               ──────────────────────    ──────────────────────
RenderLiveCommand.Run()  ──→  InspectIpcServer          
  InspectIpcClient               accept loop task       
    OpenPipeAsync()        ←─   WaitForConnectionAsync  
    SendRequestAsync()     ──→  HandleConnectionAsync   
                                  BeginInvoke ─────────→ handler(req)
                                  TCS.Task.WaitAsync(2s) ←── TCS.SetResult()
    ReadResponseAsync()    ←─   WriteResponseAsync
```

- **4 parallel accept loops** on thread-pool threads — each creates a fresh `NamedPipeServerStream` per accepted connection.
- **UI-thread dispatch**: `_uiControl.BeginInvoke(new Action(...))` + `TaskCompletionSource<InspectResponse>` bridge. Handler has a 2-second budget; `TimeoutException` returns an error response.
- **Client** (`InspectIpcClient`): connects, writes length-prefixed request, reads length-prefixed response, disconnects. No persistent connection.

## Pipe ACL (current-user FullControl)

`PipeSecurity` is built at server start:

```csharp
var ps = new PipeSecurity();
var sid = WindowsIdentity.GetCurrent().User!;
ps.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
```

Only the current user's SID gets `FullControl`. Other accounts cannot connect. ACL build failure logs a warning and skips server start rather than crashing the tray.

## Cross-references

- JSON schema details: `docs/dashboard-inspect-schema.md`
- Dev build marker: `dev-build-marker-logging.md` (this wiki)
- Render verb (offline DrawToBitmap): `render-verb-architecture.md` (this wiki)

**Discovered:** During live-inspect step 09 source-doc rollup (schema drift check against as-built code).
**Impact:** Agents using inspect-live must use `controlPath` (not `nodeIndex`) to identify findings, and must handle `"info"` severity. FormGeometry field names differ from the intuitive `x`/`y`/`width`/`height`.

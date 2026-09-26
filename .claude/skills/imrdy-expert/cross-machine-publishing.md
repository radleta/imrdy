---
tags: [imrdy-expert/publishing]
summary: "Cross-machine publishing map: sinks and the endpoint-is-direction rule, the wire listener's pre-auth guards, the ownership guard that fails closed, the snapshot's Skip/Publish/Retire decision with no age term, the two hosts, and receiver-side ingest and activation"
last-verified: "2026-09-26"
---

# Cross-Machine Publishing

The whole publish/receive stack lives in `src/Imrdy.Core/Publishing/` with no WinForms, so the
Windows tray and the Linux daemon run the same code. A change there has to build and run under
both hosts ([build-dev-cross-platform](build-dev-cross-platform.md)). Most of these classes state
their reasons in their own XML docs; this page says which file owns which decision and what not to
undo. Liveness is on [Publisher Liveness](publisher-liveness.md), the Connections window and
`imrdy links` on [Connections Surfaces](connections-surfaces.md), and every file this stack writes
on [Tray Persistence Verbs](tray-persistence-verbs.md).

## Sinks and links

A publisher writes only through
[`ISessionSink`](../../../src/Imrdy.Core/Publishing/ISessionSink.cs) (`PublishAsync`,
`RemoveAsync`, `Health`), and a sink carries the state as the hooks wrote it plus `origin_machine`
— no per-receiver filtering or projection.

- **`FileSink`** writes straight into another machine's sessions directory over a mount. Never a
  rename, for the reason on [State File Write Path](state-file-write-path.md). Its health is always
  `FileSink`, never `Connected`: a mount has no connection to report.
- **`TcpSink`** dials in the background with 1 s → 30 s exponential backoff, sends `hello` then a
  full connect snapshot, and **drops events while the link is down, never queues them**. The
  connect snapshot on reconnect is what restores current state.
- **[`SinkFactory.cs`](../../../src/Imrdy.Core/Publishing/SinkFactory.cs)** picks the sink by endpoint
  shape: `host:port` first, then a rooted path; anything else is skipped with a Warning.
  `IsFileEndpoint` answers from the same two branches in the same order — keep them in lockstep, or
  a link is reported as one kind and built as the other. A Windows drive letter must not parse as
  `host:port`.
- **The endpoint is the direction.** A `publishers.json` record with an `endpoint` is dialed; one
  without is receive-only — a receiver registers a publisher only to hold its `desktop_index` and
  `muted`, and must not dial it back. `TryCreate` returns null for a null endpoint *silently*; an
  empty-string endpoint is malformed and still warns.
- **[`SinkRegistry.cs`](../../../src/Imrdy.Core/Publishing/SinkRegistry.cs)** caches one sink per link
  and rebuilds a sink only when its record changes, so health and live connections survive a config
  reload. `Health` never reconciles — a read-only surface that reconciled would stall the UI thread
  for each evicted TCP link — so a new mutation path calls `Reconcile()` where records change.
- A CLI process registers no sink and no listener: it reports on links, never opens them.

`network` in `config.json` holds the scalars: `machineName` (null → the hostname, or
`<hostname>-<distro>` inside WSL, via `MachineNameResolver`), `authKey`, `listenPort` (clamped in
`EnsureDefaults`) and `listenEnabled` (default false). Live reload of that section is on
[Config Live Reload](config-live-reload.md).

## The wire and the listener

Newline-delimited UTF-8 JSON, capped at `WireProtocol.MaxLineBytes`. One `WireFrame` record with a
`type` discriminator — `hello`, `session`, `remove` — and null fields omitted, so exactly three
shapes cross the wire. `hello` is authenticated on the shared key and the schema **major** version.

[`WireListener.cs`](../../../src/Imrdy.Core/Publishing/WireListener.cs) treats everything before
`hello` as attacker-shaped, and its four guards stay when the accept path changes: a cap on
concurrent connections, a deadline for `hello`, an escaped and bounded peer machine name before it
becomes a key or a log field, and a cap on the link table — `Refuse` runs before authentication, so
an uncapped table is one a peer can grow. The limits are the constants at the top of the file.

`network.authKey: null` accepts every peer that reaches the port. That is a Warning at bind and a
visible state on the listening line of both connection surfaces; keep it visible if either surface
changes.

An unknown frame `type` is skipped — the peer is only newer. A session id that would compose an
unsafe path is the opposite case (see ingest below): the peer is hostile and the link is torn down.
Keep the two cases distinct.

## Publish path and the ownership guard

`SessionDirectoryWatcher` (a policy-free FSW adapter) → `SessionChangeQueue` (at most one emit per
session per drain, last event wins) → `SessionPublisher.DrainAsync` (change kind → snapshot, delta
or removal; each sink's failure isolated, nothing queued; sinks resolved per emit, so a config
reload takes effect).

**A session is published only by the machine it started on.**
[`SessionPublisher.cs` `IsLocallyOwned`](../../../src/Imrdy.Core/Publishing/SessionPublisher.cs) is
`OriginMachine is null`; a state file carrying `origin_machine` is never re-published. Removals take
the same guard from what the publisher recorded when it last read the file, since the file is gone
by then, and an **unknown origin fails closed** — not mirrored — because a wrongly mirrored removal
deletes another machine's live session. Ownership has two writers and both are needed:
`RecordOwnership` on every state file the publisher reads, and `TrayApp.RemoveSession`, which
records the entry it still holds before deleting the file.

## What a snapshot sends

`SessionPublisher.SnapshotActionFor` returns `Skip` (not locally owned), `Publish`
(`SessionDisplayFilter.WouldDisplay`: status is not `end`) or `Retire` (ended — send a `remove`).
Its XML doc gives the full reasoning; the rules it protects:

- **Retire, never merely skip, an ended session.** Nothing sweeps a publisher's sessions directory
  and deltas are dropped while a link is down, so a missed `end` skipped by every later snapshot
  would leave the receiver drawing a dead session until someone cleared it by hand.
- **One decision, two call sites:** `PublishSnapshotAsync` and `TcpSink.SendConnectSnapshotAsync`.
  `SinkContext.LocalSnapshot` filters on ownership only; keep the display decision out of it,
  because retiring is a frame and a list cannot express a frame by omission.
- **Snapshots only.** Filtering deltas would suppress the very event that tells a receiver a
  session ended.
- **No age term, in any form.** A 60-minute window was built and rejected by the user: a session
  that has gone quiet is exactly what imrdy exists to report. Too many *live* sessions is a
  presentation problem; a legacy backlog is cleared with **Clear sessions**. The filter does not
  sweep the publisher's disk either.

## Hosts

- **Linux:** `imrdy daemon` (`DaemonCommand` + `DaemonHost`) holds `DaemonLock`, sends the connect
  snapshot and drains until cancelled; the hook spawns it only when an enabled link exists
  ([Architecture](architecture.md#the-linux-binary-srcimrdylinuxprogramcs)).
- **Windows:** the tray is publisher and receiver. `InitializePublishing` builds the publisher and
  sends a startup snapshot, `StartWireListener` binds when `listenEnabled`, and `PumpPublishing`
  drains off-thread from the existing 100 ms tick. **Do not add a `SessionDirectoryWatcher` to
  `TrayApp`**: its existing session-watcher handlers already enqueue into the `SessionChangeQueue`,
  and a second watcher on that directory doubles every event.

## Receiver side

**Ingest is the trust boundary.** [`SessionIngest.cs`](../../../src/Imrdy.Core/Publishing/SessionIngest.cs)
is the only writer of remote session files, shared by `FileSink` and `WireListener`. Its session id
arrives off the wire or another machine's mount, so both verbs return false rather than write
unless `SessionIdValidator.IsValid` passes and the composed path stays inside the sessions
directory. It stamps `origin_machine` itself — escaped and bounded — and never trusts the payload's,
so no later reader of that file (log, tooltip, dashboard chip, Connections window) sanitizes it
again; a new remote-sourced field gets the same treatment here, not at each reader. The merge
keep-list and the skip-if-unchanged write are on [Tray Persistence Verbs](tray-persistence-verbs.md).

**Activation** of a remote session goes to its receiver-side `DesktopIndex`, else its publisher's
`desktop_index`, else nowhere — with no window lookup and no terminal focus, unless
`MachineNameResolver.IsSameMachine` says the publisher is this box, which takes the local path
([WT Desktop Routing](wt-desktop-routing.md)). `BootstrapSessions` loads already-assigned sessions
first, so an unassigned remote session seen at startup inherits a sibling's desktop rather than the
startup desktop; keep that ordering.

**Other receiver rules:**

- `WorkspaceVisibility` ignores sessions carrying `origin_machine`, so a remote checkout at the same
  path never hides a local workspace chip.
- `PublisherEntry.Muted` suppresses toast and sound in the dwell-fire path, *after* the dwell has
  run; the icon still updates.
- A disconnected publisher's sessions are never evicted automatically; **Clear sessions**
  (`ClearSessionsFromMachine`) is the only way they go.

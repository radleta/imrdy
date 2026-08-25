---
tags: [imrdy-expert/hooks]
summary: "All 20 Claude Code hook events — what they send, status mapping, the background_tasks roster on Stop/SubagentStop and its type-dependent entry shape, and real-world behavior"
---

# Hook Events

`plugin/hooks/hooks.json` registers 20 Claude Code hook events. Each fires `imrdy hook`, which reads JSON from stdin, derives status via `StatusDerivation`, and writes a state file.

**The manifest is not the source of truth for what actually fires** — when imrdy is wired through `~/.claude/settings.json` rather than the plugin, only the events listed there arrive. Verify against `~/.imrdy/logs/hook__*.log` before assuming an event is reaching imrdy (see "Registration is what actually fires" below).

See [Status Mapping](status-mapping.md) for the two-layer mapping from hook event → base status → color.

## Event-to-Status Mapping

| Event | Status | Notes |
|-------|--------|-------|
| SessionStart | start (→idle) | source="resume" → idle instead. `source` `startup`/`resume` also **clears** the stored roster (process boundary) |
| UserPromptSubmit | busy | User typed a prompt |
| PreToolUse | busy | About to use a tool |
| PostToolUse | busy | Tool use completed |
| PostToolUseFailure | error | Tool use failed |
| PreCompact | compact | Context compaction starting |
| PostCompact | idle | Compaction finished |
| Stop | idle | **Authoritative "waiting for the user" signal** (lead only). Carries `background_tasks` |
| StopFailure | error | Stop failed |
| Notification | attention | Special cases: idle_prompt→idle, permission_prompt→permission |
| PermissionRequest | permission | Claude needs user approval |
| PermissionDenied | idle | User denied — Claude returns to prompt, waiting for user input |
| SubagentStart | (no change) | Subagent lifecycle — never moves lead status |
| SubagentStop | (no change) | Subagent lifecycle — never moves lead status. Carries `background_tasks` |
| Elicitation | permission | Interactive prompt to user |
| WorktreeCreate | busy | Worktree created for isolated work |
| TaskCreated | (no change) | Subagent lifecycle — never moves lead status |
| TaskCompleted | (no change) | Subagent lifecycle — never moves lead status |
| TeammateIdle | (no change) | Subagent lifecycle — never moves lead status |
| SessionEnd | end | Session terminated |

## Key Behavioral Discoveries

### Stop IS idle (corrected 2026-08-20)
Earlier guidance said Stop fires between teammate coordination turns and therefore doesn't mean
idle, so Stop was mapped to "done" (teal). **Measurement disproved this.** Across 1341 hook events
from three heavy-subagent sessions, all 40 lead `Stop` events were followed by either
`Notification/idle_prompt` (26) or `UserPromptSubmit` (14) — never by more lead work. Subagent turn
ends do not surface as a lead `Stop`; a lead `Stop` fires only when the main agent's turn is over.

`Stop` (without `agent_id`) is now the primary "waiting for the user" signal, and `idle_prompt` is
a 60s confirmation backstop rather than the sole authority.

### PermissionDenied → idle (not busy)
Initially mapped to "busy" assuming Claude would process the denial. Real-world testing showed Claude returns to the user prompt after denial — it's waiting for input, not thinking. Purple icon now immediately clears to green on deny.

### idle_prompt is a confirmation backstop
Notification with notification_type="idle_prompt" fires ~60 seconds after the lead's last activity
and repeats while the session stays idle. It confirms what `Stop` already established. It is no
longer suppressed when teammates are active — that suppression was destroying the signal. See
[Teammate Detection](teammate-detection.md).

### agent_id presence is the teammate gate
Events with `agent_id` are from subagents; events without are from the lead. `HookCommand`
delegates to `TeammateGate.ApplyTeammateEvent()`: subagent events supply the `background_tasks`
roster and refresh `timestamp`, never lead status — except to clear a `permission` the subagent
resolved. Lead events do full state file writes.

Caveat: subagent *lifecycle* events (SubagentStart/Stop, TaskCreated/Completed, TeammateIdle) can
arrive **without** `agent_id`, because the parent spawns and reaps the subagent. The `agent_id`
gate alone does not catch them — `TeammateGate.IsSubagentLifecycleEvent()` filters them on the lead
path. See [Teammate Detection](teammate-detection.md).

### High-frequency events
PreToolUse fires once per tool use — potentially hundreds per minute. In observed multi-agent work
it is **87% of all hook traffic** (1056 subagent vs 167 lead out of 1341 events). Subagent
PreToolUse takes the cheap teammate path — it carries no `background_tasks`, so it refreshes the
timestamp and leaves both the lead status and the stored roster alone.

### Registration is what actually fires, not the plugin manifest
`plugin/hooks/hooks.json` registers 20 events, but the plugin is only one possible source. When
imrdy is wired through `~/.claude/settings.json` instead (the common dev setup), only the events
listed *there* fire — the manifest is inert.

This bit once: settings.json carried only 8 of the 20, so `PostToolUse`, `PostToolUseFailure`,
`PostCompact`, `StopFailure`, `PermissionDenied`, `SubagentStart`, `SubagentStop`, `TaskCreated`,
`TaskCompleted`, `TeammateIdle`, `Elicitation`, and `WorktreeCreate` fired **zero** times across
1215 tool uses. `ShouldClearPermission` depends on `PostToolUse` / `PostToolUseFailure` /
`PermissionDenied`, so that path was silently unreachable. Fixed 2026-08-20 by bringing
settings.json to full parity with the manifest.

**Keep them in parity.** The settings files live in `claude-code-ref`
(`.claude-win-personal/settings.json`, `.claude-linux-personal/settings.json`) and are symlinked
into `~/.claude/`. When updating: derive the event list from `plugin/hooks/hooks.json` rather than
hand-maintaining it; *append* the imrdy entry to an event's array instead of replacing it, because
other tools share `PreToolUse` and `UserPromptSubmit`; and preserve each file's CRLF endings
(Python's text-mode read silently converts them, which rewrites every line on save).

**settings.json hot-reloads.** New hook registrations take effect in already-running sessions
within seconds — no restart needed. Verified by watching `PostToolUse` and `SubagentStop` begin
arriving in `hook__*.log` immediately after the edit.

When debugging "status never changes", check the actual registration source before the code.

As of 2026-08 Anthropic documents ~31 hook events (adding `PostToolBatch`, `MessageDisplay`,
`UserPromptExpansion`, `InstructionsLoaded`, `ConfigChange`, `CwdChanged`, `FileChanged`,
`ElicitationResult`, `WorktreeRemove`, `Setup`, `DirectoryAdded`) and notification types
`agent_needs_input` / `agent_completed` alongside `idle_prompt` / `permission_prompt`.

## Fields on Hook Events

Standard fields on every event: `hook_event_name`, `session_id`, `cwd`, `session_name`.

| Field | When Present |
|-------|-------------|
| source | SessionStart (e.g., "startup", "resume", "clear", "compact") |
| notification_type | Notification (e.g., "idle_prompt", "permission_prompt") |
| prompt | UserPromptSubmit |
| message | Stop (last_assistant_message), Notification |
| tool_name | PreToolUse, PostToolUse, PostToolUseFailure |
| agent_id | Any event from a teammate/subagent |
| agent_type | Any event from a teammate (e.g., "worker") |
| **background_tasks** | **Stop and SubagentStop only** — see below |
| agent_transcript_path | SubagentStop (96/96 across `evidence/capture.log`) |
| is_interrupt | PostToolUseFailure (12/12 across `evidence/capture.log`; every observed value was `false`) |
| duration_ms | PostToolUse (279/279) and PostToolUseFailure (12/12) across `evidence/capture.log` |

`agent_transcript_path`, `is_interrupt`, and `duration_ms` are recorded here because they are
**observed on the wire**, not because imrdy consumes them. imrdy models none of the three; they
land in `[JsonExtensionData]` on `HookEventModel` and appear in the hook log line. Using
`agent_transcript_path` or `duration_ms` for anything is explicitly out of scope — do not write
code that reads them without a decision to do so.

Undocumented fields land in `[JsonExtensionData]` on `HookEventModel` and are logged in the single-line hook log format.

## `background_tasks` — the running-work roster

`Stop` and `SubagentStop` payloads carry a top-level `background_tasks` array listing everything
still running for the session. **Only those two events carry it**, and both carry it always:
across the whole of `evidence/capture.log`, **13/13 `Stop`** and **96/96 `SubagentStop`** payloads
have the key, and no other event type has it at the top level.

> Parse, do not grep. A naive substring grep over `capture.log` also reports hits on `PreToolUse`,
> `PostToolUse`, and `PostToolUseFailure`. All of them are the circular-capture artifact — the
> harness recording its own analysis text inside a rendered `tool_input` — and none has the key at
> the top level.

imrdy deserializes the array into `List<BackgroundTaskModel>?` on `HookEventModel` and persists it
as `running_tasks` on the state file. `null` (field absent) and `[]` (field present, measured
empty) are **different facts** and are never collapsed into each other. See
[Teammate Detection](teammate-detection.md) for the storage, clearing, and display rules.

### Entry shape is type-dependent

Two `type` values are observed across the 277 roster entries in `evidence/capture.log` — 216
`subagent` and 61 `shell` — and **the two shapes differ in which keys are present**:

```json
[{"id":"sh3n7qxk","type":"shell","status":"running",
  "description":"find . -iname \"*.dll\" -path \"*Imrdy.Core*\" 2>/dev/null | head -20",
  "command":"find . -iname \"*.dll\" -path \"*Imrdy.Core*\" 2>/dev/null | head -20"},
 {"id":"7f0c1a2b3d4e5f6a7","type":"subagent","status":"running",
  "description":"Investigate overlay chip cache invalidation","agent_type":"general-purpose"}]
```

| `type` | Key set (exact) | Count |
|---|---|---|
| `shell` | `id`, `type`, `status`, `description`, **`command`** — no `agent_type` | 61/61 |
| `subagent` | `id`, `type`, `status`, `description`, **`agent_type`** — no `command` | 216/216 |

Six distinct keys are observed across the corpus, and **no entry carries all six**. A shell entry
has `command` and lacks `agent_type`; a subagent entry has `agent_type` and lacks `command`.

Two things about that are easy to get backwards:

- **`agent_type` is *absent* on shell entries, never present-as-null.** All 61 shell entries omit
  the key outright. An absent JSON key deserializes to `null` for a `string?` property, which is
  the only way `BackgroundTaskModel.AgentType` ever ends up null. If you see `"agent_type": null`
  written on a shell entry in a distilled or summarised view of this data, that is the distiller
  filling in a missing key — it is not the wire shape. A test asserting "`agent_type` is present
  and null" is asserting a distillation artifact.
- **`command` is a sixth, deliberately unmodelled key.** `BackgroundTaskModel` models five fields
  (`id`, `type`, `status`, `description`, `agent_type`) and does not model `command`, because it
  can be an arbitrarily long shell string and `description` already summarises it. So the correct
  generalisation is over the six *observed* keys, not the five *modelled* ones — every subagent
  entry does carry all five modelled fields.

`BackgroundTaskModel` has no `[JsonExtensionData]` member, so the unknown `command` key is dropped
harmlessly on deserialization and does not round-trip to the state file. Unknown members in general
deserialize without error — a future Claude Code build adding a key will not break the hook path.

Every observed entry is `status: "running"` — **277/277 across `evidence/capture.log`** — but
imrdy counts entries without inspecting that field, since filtering on a one-member vocabulary
would fail silently the day the vocabulary changed. Each entry's `status` is emitted on the
`tasks=` token of the hook log line so drift shows up in the logs rather than being guessed at.

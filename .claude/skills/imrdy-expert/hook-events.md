---
tags: [imrdy-expert/hooks]
summary: "All 20 Claude Code hook events — what they send, status mapping, and real-world behavior"
---

# Hook Events

`plugin/hooks/hooks.json` registers 20 Claude Code hook events. Each fires `imrdy hook`, which reads JSON from stdin, derives status via `StatusDerivation`, and writes a state file.

**The manifest is not the source of truth for what actually fires** — when imrdy is wired through `~/.claude/settings.json` rather than the plugin, only the events listed there arrive. Verify against `~/.imrdy/logs/hook__*.log` before assuming an event is reaching imrdy (see "Registration is what actually fires" below).

See [Status Mapping](status-mapping.md) for the two-layer mapping from hook event → base status → color.

## Event-to-Status Mapping

| Event | Status | Notes |
|-------|--------|-------|
| SessionStart | start (→idle) | source="resume" → idle instead |
| UserPromptSubmit | busy | User typed a prompt |
| PreToolUse | busy | About to use a tool |
| PostToolUse | busy | Tool use completed |
| PostToolUseFailure | error | Tool use failed |
| PreCompact | compact | Context compaction starting |
| PostCompact | idle | Compaction finished |
| Stop | idle | **Authoritative "waiting for the user" signal** (lead only) |
| StopFailure | error | Stop failed |
| Notification | attention | Special cases: idle_prompt→idle, permission_prompt→permission |
| PermissionRequest | permission | Claude needs user approval |
| PermissionDenied | idle | User denied — Claude returns to prompt, waiting for user input |
| SubagentStart | (no change) | Subagent lifecycle — never moves lead status |
| SubagentStop | (no change) | Subagent lifecycle — never moves lead status |
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
delegates to `TeammateGate.ApplyTeammateEvent()`: subagent events only refresh `last_teammate_at`
(liveness for icon aging), never lead status — except to clear a `permission` the subagent
resolved. Lead events do full state file writes.

Caveat: subagent *lifecycle* events (SubagentStart/Stop, TaskCreated/Completed, TeammateIdle) can
arrive **without** `agent_id`, because the parent spawns and reaps the subagent. The `agent_id`
gate alone does not catch them — `TeammateGate.IsSubagentLifecycleEvent()` filters them on the lead
path. See [Teammate Detection](teammate-detection.md).

### High-frequency events
PreToolUse fires once per tool use — potentially hundreds per minute. In observed multi-agent work
it is **87% of all hook traffic** (1056 subagent vs 167 lead out of 1341 events). Subagent
PreToolUse takes the cheap teammate path (timestamp refresh only).

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
| source | SessionStart (e.g., "resume") |
| notification_type | Notification (e.g., "idle_prompt", "permission_prompt") |
| prompt | UserPromptSubmit |
| message | Stop (last_assistant_message), Notification |
| tool_name | PreToolUse, PostToolUse, PostToolUseFailure |
| agent_id | Any event from a teammate/subagent |
| agent_type | Any event from a teammate (e.g., "worker") |

Undocumented fields land in `[JsonExtensionData]` on `HookEventModel` and are logged in the single-line hook log format.

---
tags: [imrdy/hooks]
updated: 2026-04-15
summary: "All 20 Claude Code hook events — what they send, status mapping, and real-world behavior"
---

# Hook Events

imrdy registers all 20 Claude Code hook events in `plugin/hooks/hooks.json`. Each fires `imrdy hook` which reads JSON from stdin, derives status via `StatusDerivation`, and writes a state file.

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
| Stop | done | Turn finished — NOT idle (see [Teammate Detection](teammate-detection.md)) |
| StopFailure | error | Stop failed |
| Notification | attention | Special cases: idle_prompt→idle, permission_prompt→permission |
| PermissionRequest | permission | Claude needs user approval |
| PermissionDenied | idle | User denied — Claude returns to prompt, waiting for user input |
| SubagentStart | busy | Teammate spawned (carries agent_id) |
| SubagentStop | busy | Teammate stopped (carries agent_id) |
| Elicitation | permission | Interactive prompt to user |
| WorktreeCreate | busy | Worktree created for isolated work |
| TaskCreated | busy | Team task delegated |
| TaskCompleted | busy | Team task finished |
| TeammateIdle | busy | Teammate went idle (carries agent_id) |
| SessionEnd | end | Session terminated |

## Key Behavioral Discoveries

### Stop ≠ idle
Stop fires between every turn, including between teammate coordination turns. Mapping Stop→idle caused false "idle" toasts during teams. Changed to Stop→"done" (teal) as intermediate status. The authoritative idle signal is `idle_prompt` Notification, which fires exactly 60 seconds after genuine inactivity.

### PermissionDenied → idle (not busy)
Initially mapped to "busy" assuming Claude would process the denial. Real-world testing showed Claude returns to the user prompt after denial — it's waiting for input, not thinking. Purple icon now immediately clears to green on deny.

### idle_prompt is the authoritative idle signal (solo sessions)
Notification with notification_type="idle_prompt" fires exactly 60 seconds after Claude's last activity. For solo sessions, this is the definitive "genuinely waiting for user" signal — the backstop for all idle detection. For team sessions, idle_prompt is suppressed when teammates are active (rewritten to "done") — consensus handles promotion instead. See [Teammate Detection](teammate-detection.md) Layer 4.

### agent_id presence is the teammate gate
Events with `agent_id` field are from teammates. Events without are from the lead. The hook command delegates to `TeammateGate.ApplyTeammateEvent()`: teammate events normally only update `last_teammate_at`, but will also clear the lead's "permission" status when the teammate fires a permission-resolution event (PostToolUse, PostToolUseFailure, PermissionDenied). Lead events do full state file writes. See [Teammate Detection](teammate-detection.md) Layer 1.

### High-frequency events
PreToolUse and PostToolUse fire once per tool use — potentially hundreds per minute on busy sessions. Both map to "busy" so PostToolUse adds no status change, but provides confirmation data in logs.

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

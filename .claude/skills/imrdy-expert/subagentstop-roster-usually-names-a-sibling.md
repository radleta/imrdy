---
tags: [imrdy-expert/measurement]
summary: "SubagentStop rosters typically name sibling agents, not self-inclusion — compare agent_id carefully"
---

## A `SubagentStop`'s non-empty roster is usually a *sibling*, not self-inclusion — compare `agent_id` to the entry ids or you will count the wrong events

Self-inclusion (D3 / RK2) means one specific thing: a `SubagentStop` payload whose own `agent_id`
appears **inside its own `background_tasks` array**. "This `SubagentStop` carries a non-empty roster"
is not that claim and does not imply it — the common case is a stopping agent correctly reporting a
*different* agent that is still running.

The denominators say so on both scales:

- **Reference corpus (`evidence/capture.log`):** 11 of 96 `SubagentStop` payloads self-include
  (11.5%; 10 of 81 in the 609-payload analysis window). So ~88% of `SubagentStop` rosters name
  someone else or are empty.
- **Step 09's live run (`live-run/capture.log`), n = 3:** exactly one self-includes —
  `15:30:19.780`, `agent_id=a61cf650d41797af6`, roster `[subagent:a61cf650d41797af6:running]`
  (`:102-103`). The other two — `15:27:42.515` (`agent_id=a767ef473fb3c4cd9`, `:74-75`) and
  `15:28:55.381` (`agent_id=a1e11457dd8722460`, `:86-87`) — each carry a roster naming
  `a61cf650d41797af6`, a third agent that was genuinely still running.

Two things make the misreading easy, and both are structural rather than careless:

1. **The INF `Hook:` line shows the roster but not reliably the actor.** `tasks=N[type:id:status]`
   renders before the `ExtensionData` loop and survives on the entry's first physical line, but the
   `[teammate agent=…]` suffix renders *after* `{Details}` (`HookCommand.cs:95`) and is stranded
   past the first newline of any multi-line payload field. So an INF line can show you a full roster
   with no visible actor, and the roster alone looks like self-inclusion. The `Hook raw stdin:` DBG
   payload is the only source carrying `agent_id` and `background_tasks` on one physical line.
2. **All three of the run's rosters were textually identical** — same single entry, same id. Two
   events reporting a sibling and one reporting itself are indistinguishable on the roster column
   alone; only the `agent_id` column separates them.

A related shape from the same run: `SubagentStop` can reach the lead stream with **no matching
`SubagentStart` in the same capture**. The subject's lead stream held 1 `SubagentStart` against 3
`SubagentStop`s, plus a fourth agent id appearing only on a `PreToolUse` (`:78-79`). Do not assume
lifecycle events pair up inside a capture window, and do not infer an agent count from
`grep -c SubagentStart`.

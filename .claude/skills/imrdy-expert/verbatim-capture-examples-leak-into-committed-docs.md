---
tags: [imrdy-expert/process-findings]
summary: "Copying examples verbatim out of evidence captures can move real third-party content into committed pages"
---

## Copying an example verbatim out of an evidence capture moves real third-party content into committed docs

Step 12 rewrote five `.claude/skills/imrdy-expert/` wiki pages during the hook-events documentation pass. The `background_tasks` example JSON in `hook-events.md` was copied byte-for-byte from `scratch/agent-liveness-roster/evidence/capture.log:14` — both `id` values, the `description` strings, and the `command` string, all verbatim.

The capture is a recording of real hook traffic from an unrelated work session. Tracing the `agent_id` back to `capture.log:3` revealed that session's `cwd` was a different, business-confidential codebase entirely, not imrdy. The content was authentic third-party task material that had no business living in a committed wiki page.

**The boundary crossed:** `scratch/` is gitignored; `.claude/skills/` is committed. Content that was fine sitting in an untracked evidence capture became a committed artifact the moment it was pasted into wiki prose.

No credentials, session ids, transcript paths, or usernames were involved, which kept the severity moderate — the leaked material was verbatim task *description* and *command* text. Still a boundary violation.

**Why the first automated sweep missed it:** the security review grepped for session ids, absolute developer paths, usernames, and transcript paths. Verbatim task description and command text match none of those patterns. A pattern-based scan cannot catch this class of leak, because the leaked material has no distinguishing shape — it is ordinary English prose and shell command text that merely happens to be real.

**The general rule:** when documenting a wire format or payload shape from a capture, invent the example values. Derived *counts* from a capture are fine and desirable — real figures are what make documentation trustworthy. Verbatim *content* is not. Keep the structural properties the example is teaching (which keys are present or absent per entry type, field values like `status`) and replace every identifier and free-text string with something obviously fabricated.

**The check that actually works:** grep each literal value of the finished example back against the source capture. Anything that matches is not synthetic, regardless of how invented it looks.

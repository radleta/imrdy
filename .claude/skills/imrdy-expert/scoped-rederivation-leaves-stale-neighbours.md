---
tags: [imrdy-expert/design]
summary: "Scoping artifact corrections to one source file leaves stale neighbor citations untouched"
---

## Scoping a re-derivation to one file leaves stale citations in the same table untouched

Step 13 tasked narrowing `.claude/skills/imrdy-expert/state-file-write-path.md`'s write-call-site inventory table from a broad audit to a targeted correction. The step scoped the line-number re-derivation to "every `HookCommand.cs` line number" — stated three separate times in the step text.

The coder followed that scope correctly and completely. While working it also noticed `TrayApp.cs:845` in the same table was stale (that row points to a blank line inside the `RemoveAfter` grace-period block; the actual `PersistSessionField` write lives at `TrayApp.cs:953`). The coder recorded the observation in its self-check and deliberately left that row unchanged because the step scoped to `HookCommand.cs` only.

That was a defensible reading of the step instruction, not a mistake by the implementation. The defect was in the step's scoping design: a known-stale citation remained two rows below the rows just corrected, within the exact artifact the step existed to make trustworthy.

The drift was pre-existing. The working tree shows `TrayApp.cs` gained only three net lines, whereas the gap between the cited line (845) and the actual write (953) spans roughly 108 lines — the staleness predates this branch by far.

**The general rule:** when a step re-derives quoted line numbers scattered across a table or block, scope the re-derivation to **the artifact** (the entire table, the entire quoted block), not to one source file within it. A reader trusts the artifact's internal consistency, not the subset of rows a plan step happened to name. This is the same principle as preferring "re-derive the whole table" over "adjust every row by an offset" — partial correctness in a quoted artifact reads as full correctness, even when the partial work was done flawlessly.

**Corollary for reviewers:** when a verifier finds an out-of-scope staleness like this, route it to the orchestrator as a scoping decision, not as a finding or silent drop. That is what happened here: the reviewer spotted `TrayApp.cs:845` during inspection, observed the step named only `HookCommand.cs`, and escalated the scope question. The orchestrator widened the re-derivation scope for iteration 2 — the coder then re-derived every remaining row in the table and the artifact became trustworthy.

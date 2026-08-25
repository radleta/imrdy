---
tags: [imrdy-expert/process-findings]
summary: "CLAUDE.local.md is gitignored local working memory — it may or may not exist on any given machine, and git status --short will never list it, so any acceptance criterion asserting otherwise is unsatisfiable"
---

## `CLAUDE.local.md` is gitignored local working memory

`CLAUDE.local.md` at the imrdy repo root is matched by the `*.local.md` pattern at `.gitignore:45`:

```
$ git check-ignore -v CLAUDE.local.md
.gitignore:45:*.local.md	CLAUDE.local.md
```

It is local working memory managed by the `local-memory` skill (Active Projects). It lives only on
the developer's machine and is never committed. Its absence from a fresh clone is by design, not an
oversight.

The practical consequence: **the file may or may not exist on any given machine.** Code, plans, and
acceptance criteria must not assume either. A step that says "edit `CLAUDE.local.md`" is really
"create-or-edit"; a step that greps it is really "grep it if it is there."

### `git status --short` will never list it

Because the file is gitignored, `git status --short` cannot report it no matter how correctly it was
written. Any acceptance criterion of the form "`git status --short` lists `CLAUDE.local.md`" is
**unsatisfiable as written** — it fails identically whether the work was done perfectly or not at
all, so it carries no signal.

`scratch/` is gitignored for the same reason (`.gitignore`, `## Scratch subrepo`) and has the same
property. Grouping a gitignored path into a `git status --short` check alongside genuinely tracked
paths is the recurring shape of this defect.

The check that actually works for a gitignored path is **existence plus content**:

```
grep -n "<entry name>" CLAUDE.local.md
```

Keep the `git status --short` assertion for the tracked paths only, and verify gitignored paths by
reading them.

### A missing path makes `grep -rn` exit non-zero without meaning "dirty"

A single `grep -rn <pattern> CLAUDE.md README.md CLAUDE.local.md .claude/skills/imrdy-expert/`
invocation exits non-zero for a path that does not exist (`No such file or directory`) while still
searching the other paths successfully. On a machine where `CLAUDE.local.md` has not been created,
that non-zero exit means **"path absent"**, not "path present and contains a forbidden symbol."

Handle it explicitly — treat a missing-file exit as a pass for that path, or create the file first —
rather than reading the exit code as a content failure.

### Where this bit

A plan authored a cross-file zero-hit grep over
`CLAUDE.md README.md CLAUDE.local.md .claude/skills/imrdy-expert/` and asserted that
`CLAUDE.local.md` "carries zero occurrences of all three symbols today" — phrasing that presumed the
file existed and was clean, when at authoring time it did not exist at all. A later step in the same
plan created it, and that step's own acceptance criteria still demanded `git status --short` list it.
Both defects trace to the same root: treating a gitignored, machine-local file as if it were a
tracked repo artifact.

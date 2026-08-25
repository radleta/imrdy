---
tags: [imrdy-expert/tooling]
summary: "mdite strips inline-code spans (backticks and their contents) from heading text before slugifying, so its anchors diverge from GitHub's — a heading with inline code cannot satisfy both renderers"
---

## mdite drops inline-code spans when generating heading anchors

Established empirically against this wiki. When mdite builds the anchor for a heading, it **removes inline-code spans entirely — the backticks *and their contents*** — before slugifying what remains. Everything after that is standard GitHub slugification: lowercase, punctuation stripped, spaces collapsed to hyphens.

GitHub does not do this. GitHub keeps the code span's text and slugifies it like any other word. The two renderers therefore produce different anchors for the same heading, and no edit to the *link* can satisfy both.

## Worked example

The heading:

```markdown
## The `Code` Word
```

is reachable under mdite only as:

```
#the-word
```

All of the following are dead anchors:

- `#the-code-word`
- ``#the-`code`-word``
- `#the--code--word`
- `#the-%60code%60-word`

The word `Code` does not appear in the anchor in any form, encoded or otherwise. It is gone.

## Real cases from this repo

| Heading | mdite anchor | NOT |
|---|---|---|
| ``## The roster-clearing rule (`ClearsRoster`, D25)`` | `#the-roster-clearing-rule-d25` | `#the-roster-clearing-rule-clearsroster-d25` |
| ``## Display resolution (`DisplayStatus`, render-time only)`` | `#display-resolution-render-time-only` | `#display-resolution-displaystatus-render-time-only` |
| ``## `background_tasks` — the running-work roster`` | `#the-running-work-roster` | `#background_tasks-the-running-work-roster` |

Note the third case: the code span was the *leading* token, so the anchor starts at the first surviving word.

## Matching tolerance

mdite is **lenient about hyphen shape** — `#x--y` and `#x-y-` both resolve to `#x-y`. Collapsed hyphen runs and leading/trailing hyphens are forgiven.

It is **strict about tokens**. A missing word or an extra word is a dead anchor, no fuzzy match. This is exactly why the inline-code stripping bites: it removes a token, and token count is the thing mdite will not forgive.

## Non-code punctuation is ordinary GitHub slugification

Confirmed against a live link in `overlay-interactivity.md` pointing into `hover-dashboard-state-machine.md`:

```
### Right-Click Does NOT Fire It (and Why That's a Constraint, Not a Gap)
→ right-click-does-not-fire-it-and-why-thats-a-constraint-not-a-gap
```

Parentheses, apostrophes, and commas are all simply stripped; the hyphen in `Right-Click` survives; case folds down. Nothing surprising happens. Only inline code is special.

## The consequence worth knowing

An anchor that satisfies mdite will **not** resolve on GitHub when the heading contains inline code — and vice versa. The two renderers cannot both be satisfied by editing the link alone, because they disagree about what the target anchor *is*.

The only fix that works in both places is **removing the backticks from the heading itself**. Once the heading has no code span, mdite and GitHub agree, and the GitHub-style slug is correct for both.

So: if a heading contains inline code and anything links to it, change the heading, not the link.

## How to check what an anchor actually is

`mdite` has no anchor-dump command. Its subcommands are `lint`, `init`, `config`, `deps`, `cat`, and `files` — none of them prints the anchor table.

Two workable methods:

1. Run `mdite lint` and read the dead-anchor errors. The error text names the anchor it could not find, which tells you what you wrote but not what it wanted — iterate.
2. Experiment in a scratch wiki: a throwaway page with the heading and a candidate link, linted in isolation.

## Where this bit

Four dead anchors were introduced across `teammate-detection.md` and `field-preservation-catalog.md` during a documentation rollup. All four pointed at headings containing inline code, written with GitHub-style slugs that included the code text.

They survived a `wiki-health` check that reported `healthy`.

**`wiki-health` and `mdite lint` do not check the same things.** `wiki-health` covers frontmatter, tags, nav summaries, and orphans. Dead anchors surface only under `mdite lint`.

A step whose acceptance criterion says "the wiki stays lint-clean" must run `mdite lint` specifically. `wiki-health` reporting `healthy` is not evidence of that, and treating it as such is how these four shipped.

## Related

- [Teammate Detection](teammate-detection.md)
- [Field Preservation Catalog](field-preservation-catalog.md)
- [Hook Events](hook-events.md)

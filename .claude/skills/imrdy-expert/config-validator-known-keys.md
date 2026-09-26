---
tags: [imrdy-expert/config, imrdy-expert/validation]
summary: "ConfigValidator keeps its own known-keys sets, compiler-unenforced — a new config.json section is a three-touch change (ImrdyConfig, EnsureDefaults, ConfigValidator), and no test notices a skipped third touch on its own"
last-verified: "2026-09-25"
---

## ConfigValidator Known Keys

[`ConfigValidator`](../../../src/Imrdy.Core/Validation/ConfigValidator.cs) maintains its own
`KnownRootKeys` and per-section `Known{Section}Keys` hash sets, used by `imrdy config validate` to
warn on unrecognized JSON keys in `config.json`. Nothing ties them to `ImrdyConfig` at compile time.

**Adding a new top-level config section is a three-touch change**, mirroring the
`FieldPreservation.PreserveFields` symmetry contract for session state:

1. Add the record to `ImrdyConfig`
2. Handle it in `ConfigReader.EnsureDefaults` (defaults, clamps)
3. Add its key to `ConfigValidator.KnownRootKeys` **plus** a `Known{Section}Keys` set, wired
   through `TryValidateSection` — one call and one set per section

Step 3 is the one that gets skipped, because skipping it still compiles and still round-trips
correctly through `ConfigReader`. The only symptom is `imrdy config validate` reporting
`Unknown key: '<section>' (possible typo)` on a legitimate section. It has been skipped twice:
`overlay` and `diagnostics` shipped without key sets, and `tray.iconStyle` was missing from
`KnownTrayKeys`, until D33 added them alongside `network`.

**The tests do not catch a skipped step 3 by themselves.**
`ConfigValidatorTests.Validate_AllRealSections_NoUnknownKeyWarnings` validates a hand-written
`config.json` listing today's sections and asserts no warnings — a new section is absent from that
JSON until someone adds it. So a new section also adds its keys to that test's JSON (and gets its
own unknown-key and not-an-object tests, as `network` has), or the gap reopens silently.

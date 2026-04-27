# Stress Test Baseline Results

Captured on developer machine after Step 8 implementation.
Run command: `dotnet test --filter "Category=Integration&FullyQualifiedName~Stress"`

## Environment

- Machine: Developer workstation (Windows 10 Pro 10.0.19045)
- .NET: 10.0
- Build: Release/publish win-x64 self-contained
- Binary: `src/Imrdy.Windows/bin/Release/net10.0-windows10.0.17763.0/win-x64/publish/imrdy.exe`

## Test 1: Sequential inspect-live x1000 (Memory Check)

**Note:** inspect-live (not render-live) is used for hermeticity — no output files, no
disk I/O cleanup. Memory profile is comparable: both verbs create/dispose a DashboardForm.
render-live additionally allocates a `Bitmap` for `DrawToBitmap`; that difference is minor
but explains any ~1–3 MiB gap if you measure both side-by-side.

| Metric | Value |
|--------|-------|
| Baseline WS (post-5s warm-up) | _pending first run_ |
| Final WS (post-5s GC wait) | _pending first run_ |
| WS growth ratio | _pending first run_ |
| Average call latency | _pending first run_ |
| P99 call latency | _pending first run_ |
| Max call latency | _pending first run_ |
| Pass/Fail | _pending first run_ |

**Tolerance band:** 10% (`RssToleranceBand = 0.10`). A 5% band would be tighter but
Windows CI VMs show >5% RSS variation from DLL mapping and background GC pressure alone.
10% provides sufficient signal for real leaks (which grow unboundedly) while avoiding
false failures from OS-level noise. Update this file after the first green CI run.

## Test 2: Malformed Request Smoke

All 6 malformed input categories must return a structured Error response and leave the
tray process alive.

| Input Class | Expected | Observed |
|-------------|----------|----------|
| Oversize body (5 KiB) | Error response | _pending_ |
| Bogus JSON (`{not-json`) | Error response | _pending_ |
| Unknown verb | Error: "unknown verb" | _pending_ |
| Missing session-id | Error response | _pending_ |
| Valid request, nonexistent session | Error response | _pending_ |
| render-live without output path | Error response | _pending_ |

## Test 3: Concurrent Connections Capped at 4

Uses `IMRDY_TEST_HOLD_HANDLE` env var to register a test-only `"ping"` verb in the
tray that blocks on a named `EventWaitHandle`. The production binary is unaffected
when the env var is absent.

| Metric | Expected | Observed |
|--------|----------|----------|
| 4 simultaneous connections held open | Yes | _pending_ |
| 5th Connect(500ms) times out | Yes | _pending_ |
| After signal: 5th Connect succeeds | Yes | _pending_ |

## Test 4: 100 Sequential inspect-live Requests

| Metric | Value |
|--------|-------|
| Calls completed | _pending_ |
| Error responses (session-not-found) | _pending_ |
| Final probe response | _pending_ |
| Tray alive at end | _pending_ |

## Regression Baseline Policy

Future changes that:
- Double baseline RSS → flag for investigation before merge
- Increase P99 latency by >2× → flag for investigation

Update the actual numbers in this file after the first green run so CI diffs show regressions.

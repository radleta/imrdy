---
tags: [imrdy-expert/testing]
updated: 2026-04-24
summary: "xunit v2 parallel test classes compete over Console.Out/Error redirects — use [Collection] attribute to serialize"
---

## xunit v2 Parallel Console Redirects Bleed Across Test Classes

When two xunit v2 test classes both redirect `Console.SetOut` or `Console.SetError` (for capturing stdout/stderr from CLI commands under test), and those classes run in parallel (xunit's default for distinct classes), one class's redirect bleeds into the other, causing cross-test output pollution.

### Symptom
A test class A redirects `Console.Error` to capture stderr from a CLI command. A sibling test class B (in a different file) also redirects `Console.Error`. When both classes run concurrently:
- Class A's test may catch stderr output from Class B's test
- Error message assertions fail intermittently (pass on slow machines, fail on fast ones)
- Output is not lost — it's captured by the wrong test

### Root Cause
`Console.SetOut` and `Console.SetError` are static process-wide state. xunit runs test classes in parallel by default (distinct classes, different assembly/namespace combinations). Two threads both calling `Console.SetError` racing on the same static field causes one's redirect to replace or interfere with the other's.

### Solution
**Add the `[Collection("CollectionName")]` attribute to every test class that redirects the console:**

```csharp
[Collection("RenderCommandConsole")]
public class RenderCommandHelpTests
{
    [Fact]
    public void Run_UnrecognizedArgs_WritesToStderrAndReturnsUserError()
    {
        var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            var result = RenderCommand.Run(["render", "unknown-flag"]);
            Assert.Equal(ExitCodes.ExitUserError, result);
            Assert.Contains("unknown flag", stderr.ToString());
        }
        finally
        {
            Console.SetError(Console.Error); // restore
        }
    }
}

[Collection("RenderCommandConsole")]
public class RenderCommandSingleTests
{
    [Fact]
    public void Run_HappyPath_WritesPngAndReturnsZero()
    {
        // ... test code
    }
}
```

- **Collection name** can be any string (e.g., `"RenderCommandConsole"`). All classes with the same collection name are serialized — they never run concurrently.
- **No collection definition class needed** — xunit creates the collection implicitly from the name string. (A collection definition class with `[CollectionDefinition]` is only needed if you want to apply shared fixtures to the collection.)

### Why It Works
xunit's parallelization strategy respects collection boundaries. All test methods in classes tagged with the same `[Collection]` name are placed in a serialization group — the test runner executes them in sequence, not in parallel. This guarantees that only one class's `Console.SetOut`/`Console.SetError` redirect is active at any moment.

### Impact for imrdy Integration Tests
Any future test class in `Imrdy.Integration.Tests` that calls `Console.SetOut`, `Console.SetError`, or any process-wide state mutation (environment variables, static singletons, file system writes to temp locations that might collide) should join an appropriate collection name to prevent cross-test contamination:

- `[Collection("RenderCommandConsole")]` — for all render-verb CLI tests
- `[Collection("PreviewDashboardConsole")]` — for preview-dashboard CLI tests (if added later)
- Or a broader `[Collection("ImrdyCliTestsSerialize")]` if many CLI commands are added

**Before:** Run the full test suite 10 times on a fast machine → intermittent failures in one or two runs (output bleeding across tests).
**After:** Consistent green across all runs — no race conditions on static console state.

### Related
- xunit documentation on [Collections and Shared Fixtures](https://xunit.net/docs/shared-context)
- xunit parallelization strategy: distinct test classes run in parallel unless they share a `[Collection]` name


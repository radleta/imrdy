---
tags: [imrdy-expert/display]
summary: "SparklineControl requires a reference time anchor for correct rendering in live and fixture-preview paths"
---

# Sparkline Reference Time Anchor

When a sparkline (or any time-window histogram) evaluates its window relative to wall-clock `DateTimeOffset.UtcNow`, fixture-based preview paths always show an empty display: all historical timestamps are outside the "last N seconds" window by definition.

## The Problem

Hardcoding `DateTimeOffset.UtcNow` inside `OnPaint` makes the control untestable with historical data and breaks all preview harnesses. The fixture's timestamps are from the past; `UtcNow` is always the present moment, so the rendering window never contains fixture data.

## The Solution

Expose a `ReferenceTime` property on the control with a default behavior:

```csharp
public DateTimeOffset ReferenceTime { get; set; } = DateTimeOffset.MinValue;

protected override void OnPaint(PaintEventArgs e)
{
    var refTime = ReferenceTime == DateTimeOffset.MinValue 
        ? DateTimeOffset.UtcNow 
        : ReferenceTime;
    
    // Use refTime for window calculations
    var windowStart = refTime.AddSeconds(-60);
    // ... render timestamps within window
}
```

The caller sets `ReferenceTime` before assigning `Timestamps`:

```csharp
_sparkline.ReferenceTime = vm.LastHookAt;
_sparkline.Timestamps = vm.Timestamps;
```

## Why This Works for Both Paths

- **Live**: `LastHookAt` is seconds old; the 60s window correctly captures recent activity relative to that anchor.
- **Fixture preview**: `LastHookAt` is the fixture's capture time; the window correctly spans the historical timestamps in the fixture, all frozen at that moment.

The semantics are identical — render timestamps within a 60-second window ending at `ReferenceTime` — whether that's wall-clock (live) or fixture time (preview).

## Anti-Pattern: Hardcoded UtcNow

The anti-pattern is checking `DateTimeOffset.UtcNow` during paint:

```csharp
// WRONG: unportable to fixtures
protected override void OnPaint(PaintEventArgs e)
{
    var now = DateTimeOffset.UtcNow;
    var windowStart = now.AddSeconds(-60);
    // ...
}
```

This breaks testability and fixture-based preview harnesses.

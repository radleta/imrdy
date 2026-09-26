---
tags: [imrdy-expert/serialization]
summary: "ImrdyJsonContext serializes only the root types it registers — register each one (List<DisplayItem> included), and reach it through the generated typed property or the concrete type, never an interface, or the published binary reads null silently"
last-verified: "2026-09-25"
code-cites:
  - src/Imrdy.Core/ImrdyJsonContext.cs
  - src/Imrdy.Windows/Rendering/OverlayRenderer.cs
---

# Source-Generated JSON Registration

imrdy serializes through one source-generated context, `ImrdyJsonContext` (`src/Imrdy.Core/ImrdyJsonContext.cs`), and ships with no reflection fallback. A type that is not reachable through it does not fail the build: the lookup comes back null and the caller produces null output. The published single-file binary is where that shows up, which is why `HookCommandRunningTasksTests` seals the roster round-trip against a Release publish.

## Register every root you serialize or deserialize

A `[JsonSerializable]` registration for a view model does not make its nested collection types available as roots of their own. Deserializing a fixture that *is* a `List<DisplayItem>` needs `[JsonSerializable(typeof(List<DisplayItem>))]` on the context even though `DashboardViewModel` is registered. Without it `OverlayRenderer` produced null for every overlay fixture — no compile error, no exception, and a null render reads like a layout bug rather than a serialization one.

## Query the concrete type, or better, the typed property

Registering `List<T>` generates metadata for `List<T>` — not an accessor for `IReadOnlyList<T>`. So:

```csharp
ImrdyJsonContext.Default.GetTypeInfo(typeof(List<DisplayItem>));          // works
ImrdyJsonContext.Default.GetTypeInfo(typeof(IReadOnlyList<DisplayItem>)); // null
```

Prefer the generated typed property, as `OverlayRenderer` does — `JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.ListDisplayItem)` — because a missing registration is then a compile error instead of a runtime null.

## Checklist

- [ ] Every type passed to `Serialize`/`Deserialize` as a root has its own `[JsonSerializable]` line on `ImrdyJsonContext`
- [ ] Callers use the typed `ImrdyJsonContext.Default.<Type>` property, or `GetTypeInfo` with the concrete type — never an interface
- [ ] A new render fixture type is checked with `imrdy render --all` and the PNGs are inspected (see [Render Verb Architecture](render-verb-architecture.md))

---
tags: [imrdy-expert/serialization]
summary: "ImrdyJsonContext must explicitly register DisplayItem and List<DisplayItem> for source-gen serialization"
code-cites: ["src/Imrdy.Core/ImrdyJsonContext.cs", "src/Imrdy.Windows/Rendering/OverlayRenderer.cs"]
---

# DisplayItem Source-Gen Registration Gotcha

When rendering `DisplayItem` collections via `ImrdyJsonContext`, the type itself must be explicitly registered with a `[JsonSerializable]` attribute — it does not inherit from implicit registrations of consuming types like `DashboardViewModel`.

## The Problem

The naive assumption is: *"If `DashboardViewModel` is registered in `ImrdyJsonContext`, then all its fields (including `List<DisplayItem>`) are automatically available for serialization."* This is **false**. Source-generated context only registers the exact types in the attribute list; nested types must be explicit.

If you attempt to serialize/deserialize `IReadOnlyList<DisplayItem>` or `List<DisplayItem>` without explicit registration:
- `OverlayRenderer.Render()` returns null on every fixture (silent failure)
- No compile-time error, no runtime exception — just null output

## The Fix

Add this to `ImrdyJsonContext.cs`:

```csharp
[JsonSerializable(typeof(List<DisplayItem>))]
public partial class ImrdyJsonContext : JsonSerializerContext
{
  // ... existing registrations
}
```

Then query the concrete type, not the interface:
```csharp
var typeInfo = ImrdyJsonContext.Default.GetTypeInfo(typeof(List<DisplayItem>));
// NOT: ImrdyJsonContext.Default.GetTypeInfo(typeof(IReadOnlyList<DisplayItem>));
```

## Why This Matters

- **Render verbs** (dashboard, overlay fixtures) deserialize from JSON fixtures into form inputs — they will produce null output silently if the type isn't registered
- **Test coverage** won't catch this (null output looks like a rendering issue, not a serialization issue)
- **Source-gen contract** is strict: only registered types can be serialized/deserialized

## Checklist

- [ ] Add `[JsonSerializable(typeof(List<DisplayItem>))]` to ImrdyJsonContext
- [ ] All calls to query the type use the concrete `List<DisplayItem>`, not interface
- [ ] Run `imrdy render --all` and verify PNG output is non-null

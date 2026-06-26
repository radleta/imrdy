---
tags: [imrdy-expert/serialization]
summary: "STJ source-gen registers concrete List<T> but callers must query by concrete type, not interface"
code-cites: []
---

# STJ Source-Gen Interface Type Caveat

When registering `List<T>` in a System.Text.Json source-generated context, the context generates `TypeInfo<List<T>>` — **not** `TypeInfo<IReadOnlyList<T>>`. Querying by the interface type silently returns null.

## The Problem

```csharp
[JsonSerializable(typeof(List<DisplayItem>))]
public partial class ImrdyJsonContext : JsonSerializerContext { }

// This works:
var typeInfo = ImrdyJsonContext.Default.GetTypeInfo(typeof(List<DisplayItem>));

// This returns null:
var typeInfo = ImrdyJsonContext.Default.GetTypeInfo(typeof(IReadOnlyList<DisplayItem>));
```

## Why

STJ's source-gen produces metadata only for the types explicitly listed in `[JsonSerializable]` attributes. When you register `List<T>`, the generator creates:
- `TypeInfo<List<T>>` ✓
- `IReadOnlyList<T>` adapter (via implicit covariance) ✓
- Direct `TypeInfo<IReadOnlyList<T>>` accessor ✗

**Source-gen does not auto-generate interface type accessors.** Callers must reference the concrete type.

## Implications

Any code that expects to query by interface type will silently fail:
```csharp
// In OverlayRenderer or other render verbs:
var typeInfo = ctx.GetTypeInfo(typeof(IReadOnlyList<DisplayItem>)); // null!
var serialized = JsonSerializer.Serialize(items, typeInfo);        // crash
```

## Checklist

- [ ] All `GetTypeInfo` queries reference concrete `List<T>`, never the interface
- [ ] All `JsonSerializer.Serialize/Deserialize` calls use concrete type info
- [ ] Test fixture deserialization with both `List<T>` and collection initializers
- [ ] Add a lint check if possible: flag any `IReadOnlyList<T>` queries against source-gen contexts

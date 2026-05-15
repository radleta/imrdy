---
tags: [imrdy-expert/winforms]
summary: "UserControl public properties of non-serializable types require DesignerSerializationVisibility attribute to avoid WFO1000 build error"
---

# WinForms Custom Property Serialization (WFO1000)

When a `UserControl` has a public property whose type is not a primitive or a type the WinForms designer can serialize automatically (e.g., `IReadOnlyList<DateTimeOffset>`, `List<string>`, custom types), the compiler raises **WFO1000**:

> Property 'X' does not configure the code serialization for its property content

With `TreatWarningsAsErrors=true` in the project file, this warning becomes a build error.

## The Fix

Decorate the property with `[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]` and add `using System.ComponentModel;`:

```csharp
using System.ComponentModel;

public partial class SparklineControl : UserControl
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<DateTimeOffset> Timestamps { get; set; }
}
```

This tells the WinForms designer to skip the property during code generation **without removing it from the public API**. The property remains usable at runtime and in code; the designer simply doesn't try to generate serialization code for it.

## When This Occurs

This is a **design-time only** constraint. The property compiles fine and works at runtime. The error only appears in the Visual Studio designer's code generation phase when:

1. The UserControl is placed in a designer (e.g., a Form or another UserControl in the editor)
2. Visual Studio generates code to serialize property values
3. The designer doesn't recognize how to serialize `IReadOnlyList<DateTimeOffset>` and raises WFO1000

## Impact

Any `UserControl` in imrdy with public properties of non-serializable types needs this attribute. It's a WinForms designer serialization requirement, not a runtime constraint. Runtime property assignment works without the attribute — the attribute only silences the designer.

## Related

If a property genuinely should not be persisted and should not appear in the designer property grid, use `[Browsable(false)]` in addition to or instead of `DesignerSerializationVisibility.Hidden`.

---
tags: [imrdy-expert/winforms]
summary: "UserControl public properties of non-serializable types require DesignerSerializationVisibility attribute to avoid WFO1000 build error"
last-verified: "2026-09-25"
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

WFO1000 is reported by `dotnet build` on the property declaration, not only inside the Visual Studio designer, and this repo's `TreatWarningsAsErrors=true` (`Directory.Build.props`) turns it into a build failure. The attribute changes nothing at runtime: the property stays public and assignable; it only tells the designer's code generator to skip it. `SparklineControl` carries it on both `Timestamps` and `ReferenceTime`.

Any `UserControl` or `Form` in imrdy that adds a public settable property of a type the designer cannot serialize needs this attribute.

## Related

If a property genuinely should not be persisted and should not appear in the designer property grid, use `[Browsable(false)]` in addition to or instead of `DesignerSerializationVisibility.Hidden`.

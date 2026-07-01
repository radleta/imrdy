---
tags: [imrdy-expert/architecture]
summary: "Imrdy.Core grants InternalsVisibleTo to assembly named 'imrdy' (the Imrdy.Windows project) — not 'Imrdy.Windows'. Internal Core classes (e.g. ControllerMenuModel) are accessible from Windows code without making them public. Use 'imrdy' (lowercase) as the assembly-name key in any future InternalsVisibleTo grant from Core."
code-cites:
  - src/Imrdy.Core/Imrdy.Core.csproj:8
---

# InternalsVisibleTo: Imrdy.Core → Imrdy.Windows (assembly name "imrdy")

## The Mechanism

`Imrdy.Core` grants `InternalsVisibleTo` to four targets via `Imrdy.Core.csproj`:

```xml
<InternalsVisibleTo Include="imrdy" />
<InternalsVisibleTo Include="Imrdy.Linux" />
<InternalsVisibleTo Include="Imrdy.Core.Tests" />
<InternalsVisibleTo Include="Imrdy.Integration.Tests" />
```

The key entry is **`imrdy`** (all lowercase) — this is the assembly name of the `Imrdy.Windows` project (the final tray binary). The project namespace is `Imrdy.Windows.*`, but the output assembly is named `imrdy`.

`Imrdy.Windows` does NOT appear in the list. Any attempt to use `InternalsVisibleTo Include="Imrdy.Windows"` would fail silently — the grant uses the assembly output name, not the project/namespace name.

## Why It Matters

`internal` classes in `Imrdy.Core` (e.g. `ControllerMenuModel`, `MenuRenderer`, `ImrdyJsonContext`) are directly callable from `Imrdy.Windows` code **without being made public**. The existing `ControllerMenuBuilder` (Windows) calling `ControllerMenuModel.Build()` (Core internal class) works because of this grant.

### Extension Rule

When adding new `internal` methods to `Imrdy.Core` classes for use by `Imrdy.Windows` code, no visibility change or new InternalsVisibleTo is needed — the grant already covers the Windows assembly. Simply promote the member from `private` to `internal` (or keep it `public` within an `internal` class).

When adding a new test project that needs Core internals, add `<InternalsVisibleTo Include="Imrdy.YourTest" />` to `Imrdy.Core.csproj`.

## Example — ControllerMenuModel.BuildOverlaySubmenu

`BuildOverlaySubmenu` is currently `private static` within `internal static class ControllerMenuModel`. Promoting it to `internal static` (or `public static` — same effective accessibility for an `internal` class) makes it callable from `OverlayMenuBuilder` in `Imrdy.Windows` **without touching csproj**, because the InternalsVisibleTo grant for `imrdy` already exists.

## Related

- `src/Imrdy.Core/Imrdy.Core.csproj` — InternalsVisibleTo declarations (lines 8-12)
- `src/Imrdy.Windows/Imrdy.Windows.csproj` — Windows-side InternalsVisibleTo to test assemblies (lines 18-19)

---
tags: [imrdy-expert/architecture]
summary: "Imrdy.Core grants InternalsVisibleTo to assembly named 'imrdy' (the Imrdy.Windows project) — not 'Imrdy.Windows'. Internal Core classes (e.g. ControllerMenuModel) are accessible from Windows code without making them public. Use 'imrdy' (lowercase) as the assembly-name key in any future InternalsVisibleTo grant from Core."
last-verified: "2026-09-25"
---

# InternalsVisibleTo: Imrdy.Core → Imrdy.Windows (assembly name "imrdy")

## The Mechanism

`Imrdy.Core` grants `InternalsVisibleTo` in [`Imrdy.Core.csproj`](../../../src/Imrdy.Core/Imrdy.Core.csproj):

```xml
<InternalsVisibleTo Include="imrdy" />
<InternalsVisibleTo Include="Imrdy.Linux" />
<InternalsVisibleTo Include="Imrdy.Core.Tests" />
<InternalsVisibleTo Include="Imrdy.Integration.Tests" />
<InternalsVisibleTo Include="Imrdy.Windows.Tests" />
```

The key entry is **`imrdy`** (all lowercase) — the `Imrdy.Windows` project sets `<AssemblyName>imrdy</AssemblyName>` so the tray binary is `imrdy.exe`. The project and its namespaces are `Imrdy.Windows.*`, but the grant keys on the assembly output name. `InternalsVisibleTo Include="Imrdy.Windows"` would name no assembly and grant nothing, silently.

`Imrdy.Windows.csproj` makes its own grants to `Imrdy.Integration.Tests` and `Imrdy.Windows.Tests`.

## Why It Matters

`internal` types in `Imrdy.Core` — `ControllerMenuModel`, `ImrdyJsonContext` and others — are directly usable from `Imrdy.Windows` code **without being made public**. `ControllerMenuBuilder` (Windows) calling into `ControllerMenuModel` (an `internal static class` in Core) works because of this grant.

### Extension Rule

When `Imrdy.Windows` code needs something from `Imrdy.Core`, make it `internal`, not `public` — the existing `imrdy` grant already covers it, and no csproj edit is needed. When adding a new test project that needs Core internals, add `<InternalsVisibleTo Include="<the test assembly name>" />` to `Imrdy.Core.csproj`, keyed on its assembly name.

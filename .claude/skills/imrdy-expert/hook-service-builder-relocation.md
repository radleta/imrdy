---
tags: [imrdy-expert/architecture]
summary: "HookServiceBuilder relocated from Imrdy.Windows.DI to Imrdy.Core.Hooks to enable cross-platform consumer access"
---

## HookServiceBuilder Location: Imrdy.Core.Hooks, Not Imrdy.Windows.DI

`HookServiceBuilder` is the lightweight DI bootstrap for the hook fast path. It provides `BuildServices()` to wire up `AddCoreServices()` and `AddSerilog()` — both platform-agnostic. Despite living in `Imrdy.Windows/DI/` in earlier phases, it had **no Windows-specific dependencies**.

**Relocation (Step 06):** Moved from `src/Imrdy.Windows/DI/HookServiceBuilder.cs` (namespace `Imrdy.Windows.DI`) to `src/Imrdy.Core/Hooks/HookServiceBuilder.cs` (namespace `Imrdy.Core.Hooks`) so platform-specific projects like `Imrdy.Linux` can reference it without cross-platform TFM boundary violations.

**Call-site transparency:** The only call site in `Imrdy.Windows/Program.cs` already had `using Imrdy.Core.Hooks` (for `HookCommand`), so the namespace change was transparent — no using-directive edits required.

**Finding rule:** For cross-platform hook consumers, always look in `Imrdy.Core.Hooks`, not `Imrdy.Windows.DI`.

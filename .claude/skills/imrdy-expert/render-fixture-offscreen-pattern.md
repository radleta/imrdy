---
tags: [imrdy-expert/testing]
summary: "Offscreen-Show pattern for deterministic Form fixture rendering in imrdy tests"
code-cites: ["tests/Imrdy.Windows/Rendering/DashboardRenderer.cs", "tests/Imrdy.Windows/Rendering/WorkspaceDashboardRenderer.cs", "tests/Imrdy.Windows/Rendering/RenderCommandAllTests.cs"]
---

# Render Fixture Harness Pattern

When testing WinForms UI rendering via `DrawToBitmap`, use the offscreen-Show pattern: create the form, position it far off-screen, call `Show()`, then `Application.DoEvents()` to force layout before drawing. This ensures:
- Form layout completes deterministically (no background threading)
- No on-screen flicker during test runs
- Bitmap captures the fully-rendered state
- Resource cleanup is guaranteed via try-finally

## The Pattern

```csharp
// Typical usage in DashboardRenderer.cs / WorkspaceDashboardRenderer.cs
private Bitmap RenderDashboard(DashboardViewModel vm)
{
    var form = new SessionDashboardForm(desktopManager: null, ctx.LoggerFactory);
    
    try
    {
        // Position off-screen to avoid visual flicker and multi-monitor conflicts
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        
        // Show() triggers WinForms layout initialization
        form.Show();
        
        // DoEvents() flushes pending layout messages to force PerformLayout()
        Application.DoEvents();
        
        // Now layout is stable; perform explicit layout if needed for complex forms
        form.PerformLayout();
        
        // Capture deterministic bitmap
        var bitmap = form.DrawToBitmap(form.ClientRectangle, PixelFormat.Format32bppArgb);
        return bitmap;
    }
    finally
    {
        form.Hide();
        form.Dispose();
    }
}
```

## Why Off-Screen Position?

- **Avoids visual flicker**: The form is shown but not visible (off all monitors)
- **Deterministic layout**: WinForms completes layout synchronously without waiting for WM_SHOWWINDOW events
- **Multi-monitor safe**: Using a fixed negative coordinate (-32000, -32000) avoids coordinate projection issues on secondary monitors
- **No activation**: Hidden location doesn't trigger focus change or taskbar entry

## Checklist for New Renderers

- [ ] Create form with `desktopManager: null` (no COM desktop interop during test)
- [ ] Set `StartPosition = FormStartPosition.Manual` before Show()
- [ ] Set `Location = new Point(-32000, -32000)` before Show()
- [ ] Call `Show()` to trigger layout initialization
- [ ] Call `Application.DoEvents()` to flush layout messages
- [ ] Call `PerformLayout()` explicitly if the form has complex nested layouts (e.g., TableLayoutPanel rows)
- [ ] Call `DrawToBitmap(form.ClientRectangle, PixelFormat.Format32bppArgb)` after layout is stable
- [ ] Wrap in try-finally with `form.Hide()` + `form.Dispose()`

## Fixture File Structure

Render fixtures live alongside the renderer:
- `tests/fixtures/dashboards/` — DashboardViewModel JSON fixtures (13 existing)
- `tests/fixtures/dashboards-bad/` — edge cases and error states (3 existing)
- `tests/fixtures/workspace-dashboards/` — WorkspaceDashboardViewModel fixtures (2 existing)
- `tests/fixtures/overlays/` — DisplayItem[] or List<DisplayItem> JSON fixtures (new in overlay phase)

Each fixture is a `.json` file deserializable via `ImrdyJsonContext.Default.GetTypeInfo(type)`.

## Testing the Renderer

```csharp
// RenderCommandAllTests.cs pattern
[Fact]
public void Run_GlobalAll_WritesAllPngsAndExitsZero()
{
    // Before adding overlay fixtures: ExpectedDashboardFixtureCount = 13 + 3 + 2 = 18
    // After adding 4 overlay fixtures: ExpectedDashboardFixtureCount = 18 + 4 = 22
    const int ExpectedDashboardFixtureCount = 22;
    
    var exitCode = RenderCommand.Run(_sp, null, null);
    
    Assert.Equal(0, exitCode);
    Assert.Equal(ExpectedDashboardFixtureCount, _pngCount);
}
```

Update `ExpectedDashboardFixtureCount` when adding new fixtures or new fixture types.

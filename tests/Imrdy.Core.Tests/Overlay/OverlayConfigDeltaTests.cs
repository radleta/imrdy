using FluentAssertions;

namespace Imrdy.Core.Tests.Overlay;

/// <summary>
/// Locks the structural-delta classification contract used by TrayApp.OnConfigChanged (Step 07).
/// Non-structural fields: Position, Monitor, Locked — zeroed via with-expression, result equals.
/// Structural fields: Enabled, Size, Spacing — differ even after zeroing, result not equal.
/// </summary>
[Trait("Category", "Unit")]
public class OverlayConfigDeltaTests
{
    private static OverlayConfig Neutralize(OverlayConfig c) =>
        c with { Position = "", Monitor = 0, Locked = false };

    // ---- Non-structural changes: neutralized pair must be equal ----

    [Fact]
    public void StructuralDelta_PositionChange_IsNonStructural()
    {
        var old = new OverlayConfig { Position = "bottom-right" };
        var updated = new OverlayConfig { Position = "top-left" };

        (Neutralize(old) == Neutralize(updated)).Should().BeTrue();
    }

    [Fact]
    public void StructuralDelta_MonitorChange_IsNonStructural()
    {
        var old = new OverlayConfig { Monitor = 0 };
        var updated = new OverlayConfig { Monitor = 1 };

        (Neutralize(old) == Neutralize(updated)).Should().BeTrue();
    }

    [Fact]
    public void StructuralDelta_LockedChange_IsNonStructural()
    {
        var old = new OverlayConfig { Locked = false };
        var updated = new OverlayConfig { Locked = true };

        (Neutralize(old) == Neutralize(updated)).Should().BeTrue();
    }

    // ---- Structural changes: neutralized pair must NOT be equal ----

    [Fact]
    public void StructuralDelta_SizeChange_IsStructural()
    {
        var old = new OverlayConfig { Size = 64 };
        var updated = new OverlayConfig { Size = 96 };

        (Neutralize(old) != Neutralize(updated)).Should().BeTrue();
    }

    [Fact]
    public void StructuralDelta_SpacingChange_IsStructural()
    {
        var old = new OverlayConfig { Spacing = 8 };
        var updated = new OverlayConfig { Spacing = 12 };

        (Neutralize(old) != Neutralize(updated)).Should().BeTrue();
    }

    [Fact]
    public void StructuralDelta_EnabledChange_IsStructural()
    {
        var old = new OverlayConfig { Enabled = false };
        var updated = new OverlayConfig { Enabled = true };

        (Neutralize(old) != Neutralize(updated)).Should().BeTrue();
    }
}

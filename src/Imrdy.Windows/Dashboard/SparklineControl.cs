using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Imrdy.Windows.Dashboard;

/// <summary>
/// Owner-drawn UserControl that renders a 60-second activity histogram.
/// Divides the 60s window into 30 buckets of 2s each; each bucket
/// is rendered as a vertical bar proportional to the event count in that bucket.
///
/// GDI resources are owned by this control and must not be shared/cached externally.
/// Dispose(bool disposing) follows the canonical pattern with an if(disposing) guard
/// so the finalizer thread never touches GDI handles.
/// </summary>
internal sealed class SparklineControl : UserControl
{
    private readonly SolidBrush _barBrush;
    private readonly Pen _axisPen;
    private IReadOnlyList<DateTimeOffset> _timestamps = Array.Empty<DateTimeOffset>();

    public SparklineControl()
    {
        // Required for owner-drawn child controls — the parent Form's DoubleBuffered
        // property does NOT propagate to child UserControls. Without these flags the
        // sparkline will flicker under the ≤100ms Update cadence.
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint,
            true);

        // Allocate owned GDI resources — do NOT use system-cached Brush/Pen objects
        // (those throw InvalidOperationException on Dispose).
        // Bar color: muted teal matching the busy-status accent (#4EC9B0 at ~55% opacity
        // blended over the dark form bg). Axis: same hue at lower brightness.
        _barBrush = new SolidBrush(Color.FromArgb(140, 78, 201, 176));
        _axisPen = new Pen(Color.FromArgb(60, 78, 201, 176), 1f);
    }

    /// <summary>
    /// Activity timestamps to visualize. Setter calls Invalidate() to trigger repaint.
    /// The caller (DashboardForm.Update) supplies the 60s-windowed list from the view model.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<DateTimeOffset> Timestamps
    {
        get => _timestamps;
        set
        {
            _timestamps = value ?? Array.Empty<DateTimeOffset>();
            Invalidate();
        }
    }

    /// <summary>
    /// Reference time for the 60s sparkline window.
    /// When set to a non-default value (e.g. vm.LastHookAt), the sparkline renders
    /// the 60s window relative to that anchor rather than DateTimeOffset.UtcNow.
    /// This is correct for both paths:
    ///  - Live: LastHookAt is seconds ago; window tracks recent activity.
    ///  - Fixture preview: LastHookAt is the fixture's capture time; window shows historical activity correctly.
    /// Default (DateTimeOffset.MinValue) falls back to DateTimeOffset.UtcNow.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateTimeOffset ReferenceTime { get; set; } = DateTimeOffset.MinValue;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        var rect = ClientRectangle;

        // Dark background matching the dashboard theme
        g.Clear(Color.FromArgb(28, 30, 38));

        const int bucketCount = 30;
        const double windowSeconds = 60.0;
        const double bucketSeconds = windowSeconds / bucketCount;

        var now = ReferenceTime == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : ReferenceTime;
        var buckets = new int[bucketCount];

        foreach (var ts in _timestamps)
        {
            var ageSeconds = (now - ts).TotalSeconds;
            if (ageSeconds < 0 || ageSeconds >= windowSeconds) continue;
            var bucketIndex = (int)(ageSeconds / bucketSeconds);
            // bucketIndex 0 = most recent; reverse so bars read left=old, right=recent
            var displayIndex = bucketCount - 1 - bucketIndex;
            if (displayIndex >= 0 && displayIndex < bucketCount)
                buckets[displayIndex]++;
        }

        var maxCount = buckets.Max();
        if (maxCount == 0)
        {
            // No activity — draw just the axis line
            g.DrawLine(_axisPen, rect.Left, rect.Bottom - 1, rect.Right - 1, rect.Bottom - 1);
            return;
        }

        var barTotalWidth = rect.Width;
        var barSlotWidth = barTotalWidth / (float)bucketCount;
        var maxBarHeight = rect.Height - 2; // leave 1px top and 1px bottom margin

        for (var i = 0; i < bucketCount; i++)
        {
            if (buckets[i] == 0) continue;

            var barHeight = Math.Max(2, (int)(maxBarHeight * buckets[i] / (double)maxCount));
            var x = (int)(i * barSlotWidth);
            var y = rect.Bottom - 1 - barHeight;
            var w = Math.Max(1, (int)barSlotWidth - 1);

            g.FillRectangle(_barBrush, x, y, w, barHeight);
        }

        // Axis baseline
        g.DrawLine(_axisPen, rect.Left, rect.Bottom - 1, rect.Right - 1, rect.Bottom - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _barBrush?.Dispose();
            _axisPen?.Dispose();
        }
        base.Dispose(disposing);
    }
}

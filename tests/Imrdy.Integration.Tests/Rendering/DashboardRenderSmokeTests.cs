using System.Drawing;
using FluentAssertions;
using Imrdy.Core.Rendering;
using Imrdy.Windows.Rendering;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Imrdy.Integration.Tests.Rendering;

/// <summary>
/// Regression-lock integration tests for <see cref="DashboardRenderer"/>.
/// Asserts every baseline fixture produces a non-collapsed layout (Height > 100),
/// writes a valid PNG to disk, AND paints actual content (not just background).
///
/// The pixel-content assertion exists because Height &gt; 100 alone is insufficient —
/// a form whose children fail to paint produces a full-height-but-blank bitmap that
/// passes a size-only check. Sampling for non-background pixels catches that class
/// of regression (lifecycle bug where CreateControl is used without Show + DoEvents,
/// or where children are added in OnLoad and never get a paint cycle).
///
/// STA threading: xunit v2 test threads are MTA. Each theory case dispatches the
/// WinForms render work onto a new STA thread via <see cref="Thread"/> with
/// <see cref="ApartmentState.STA"/>, then re-throws any exception on the test thread.
/// </summary>
[Trait("Category", "Integration")]
public class DashboardRenderSmokeTests
{
    // DashboardForm.BgForm = Color.FromArgb(28, 30, 38). Any pixel matching exactly
    // is treated as background; the rest counts as painted content.
    private const int BgR = 28;
    private const int BgG = 30;
    private const int BgB = 38;

    private static string FixturePath(string name) => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "tests", "fixtures", "dashboards", name));

    [Theory]
    [InlineData("fresh-idle.json")]
    [InlineData("long-busy.json")]
    [InlineData("aged-done.json")]
    [InlineData("many-subagents.json")]
    [InlineData("wsl-ubuntu-22.json")]
    public void DashboardRender_ProducesNonCollapsedLayout(string fixture)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            RenderResult? result = null;
            Exception? threadEx = null;

            var thread = new Thread(() =>
            {
                try
                {
                    var ctx = new RenderContext(
                        Args: [FixturePath(fixture)],
                        OutputPath: outputPath,
                        LoggerFactory: NullLoggerFactory.Instance,
                        RepoRoot: null);

                    result = new DashboardRenderer().Render(ctx);
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (threadEx is not null)
                throw new InvalidOperationException($"STA thread threw: {threadEx.Message}", threadEx);

            result.Should().NotBeNull("render must return a result");
            result!.Success.Should().BeTrue(because: result.Error);
            result.Height.Should().BeGreaterThan(100,
                because: $"{fixture} must render with its middle content band intact");
            result.Width.Should().BeGreaterThanOrEqualTo(400,
                because: "dashboard target width is 460px");
            File.Exists(outputPath).Should().BeTrue();

            var nonBgRatio = MeasureNonBackgroundRatio(outputPath);
            nonBgRatio.Should().BeGreaterThan(0.05,
                because: $"{fixture} must paint visible content, not just a blank background " +
                         $"(actual non-background ratio: {nonBgRatio:P2})");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    /// <summary>
    /// Returns the fraction of pixels that differ from the form's background color.
    /// A blank-background regression scores near 0; a fully painted dashboard scores
    /// well above 0.05 because every label, panel, status pill, and footer chip
    /// contributes non-background pixels.
    /// </summary>
    private static double MeasureNonBackgroundRatio(string pngPath)
    {
        using var bmp = new Bitmap(pngPath);
        var nonBg = 0L;
        var total = (long)bmp.Width * bmp.Height;
        for (var y = 0; y < bmp.Height; y++)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                var p = bmp.GetPixel(x, y);
                if (p.R != BgR || p.G != BgG || p.B != BgB)
                    nonBg++;
            }
        }
        return (double)nonBg / total;
    }
}

using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Windows.Forms;
using Imrdy.Core;
using Imrdy.Core.Display;
using Imrdy.Core.Graphics;
using Imrdy.Core.Rendering;
using Imrdy.Windows.Overlay;

namespace Imrdy.Windows.Rendering;

/// <summary>
/// Renders an <see cref="OverlayPanel"/> from a <c>List&lt;DisplayItem&gt;</c> fixture JSON
/// and captures the result via <see cref="System.Windows.Forms.Control.DrawToBitmap"/> to a PNG.
///
/// The caller is responsible for invoking this on an STA thread — WinForms
/// <c>DrawToBitmap</c> requires STA apartment state.
/// </summary>
internal sealed class OverlayRenderer : IRenderableSurface
{
    // Offscreen sentinel — Windows treats coordinates this far outside any monitor as
    // effectively hidden. Used to make Show() invisible during headless capture.
    private const int OffscreenX = -32000;
    private const int OffscreenY = -32000;

    public string Name => "overlay";
    public string Description => "OverlayPanel rendered from a List<DisplayItem> fixture.";
    public string? DefaultFixtureDir => "tests/fixtures/overlays";
    public string DefaultOutputExtension => "png";

    public RenderResult Render(RenderContext ctx)
    {
        try
        {
            if (ctx.Args.Length < 1 || string.IsNullOrWhiteSpace(ctx.Args[0]) || !File.Exists(ctx.Args[0]))
                return new RenderResult(false, "fixture path missing or not found", 0, 0);

            List<DisplayItem>? items;
            try
            {
                var bytes = File.ReadAllBytes(ctx.Args[0]);
                items = JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.ListDisplayItem);
            }
            catch (Exception ex)
            {
                return new RenderResult(false, $"fixture parse failed: {ex.Message}", 0, 0);
            }

            if (items is null)
                return new RenderResult(false, "fixture parse failed: deserialized to null", 0, 0);

            using var panel = new OverlayPanel(
                new OverlayConfig(),
                NullSessionInteractionRouter.Instance,
                desktopManager: null,
                ctx.LoggerFactory,
                new GraphicsPackLoader());

            // CreateControl() alone gives the panel a handle but child controls never paint —
            // their OnLoad/OnPaint cycles never fire without the message pump. The result is a
            // bitmap with only the panel background. Show() offscreen + DoEvents() drains the
            // pending paint cycle for every child, so DrawToBitmap captures the full layout.
            panel.StartPosition = FormStartPosition.Manual;
            panel.Location = new Point(OffscreenX, OffscreenY);
            panel.Show();
            try
            {
                Application.DoEvents();
                panel.LoadFixtureItems(items);
                panel.PerformLayout();

                using var bmp = new Bitmap(panel.Width, panel.Height);
                panel.DrawToBitmap(bmp, new Rectangle(0, 0, panel.Width, panel.Height));

                Directory.CreateDirectory(Path.GetDirectoryName(ctx.OutputPath)!);
                bmp.Save(ctx.OutputPath, ImageFormat.Png);

                return new RenderResult(true, null, panel.Width, panel.Height);
            }
            finally
            {
                panel.Hide();
            }
        }
        catch (Exception ex)
        {
            return new RenderResult(false, ex.Message, 0, 0);
        }
    }
}

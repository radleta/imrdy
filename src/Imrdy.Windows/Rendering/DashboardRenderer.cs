using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Windows.Forms;
using Imrdy.Core;
using Imrdy.Core.Display;
using Imrdy.Core.Rendering;
using Imrdy.Windows.Dashboard;

namespace Imrdy.Windows.Rendering;

/// <summary>
/// Renders a <see cref="DashboardForm"/> from a <see cref="DashboardViewModel"/> fixture JSON
/// and captures the result via <see cref="System.Windows.Forms.Control.DrawToBitmap"/> to a PNG.
///
/// The caller is responsible for invoking this on an STA thread — WinForms
/// <c>DrawToBitmap</c> requires STA apartment state.
/// </summary>
internal sealed class DashboardRenderer : IRenderableSurface
{
    // Offscreen sentinel — Windows treats coordinates this far outside any monitor as
    // effectively hidden. Used to make Show() invisible during headless capture.
    private const int OffscreenX = -32000;
    private const int OffscreenY = -32000;

    public string Name => "dashboard";
    public string Description => "DashboardForm rendered from a DashboardViewModel fixture.";
    public string? DefaultFixtureDir => "tests/fixtures/dashboards";
    public string DefaultOutputExtension => "png";

    public RenderResult Render(RenderContext ctx)
    {
        try
        {
            if (ctx.Args.Length < 1 || string.IsNullOrWhiteSpace(ctx.Args[0]) || !File.Exists(ctx.Args[0]))
                return new RenderResult(false, "fixture path missing or not found", 0, 0);

            DashboardViewModel? vm;
            try
            {
                var bytes = File.ReadAllBytes(ctx.Args[0]);
                vm = JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.DashboardViewModel);
            }
            catch (Exception ex)
            {
                return new RenderResult(false, $"fixture parse failed: {ex.Message}", 0, 0);
            }

            if (vm is null)
                return new RenderResult(false, "fixture parse failed: deserialized to null", 0, 0);

            var validationError = ValidateFixture(vm);
            if (validationError is not null)
                return new RenderResult(false, validationError, 0, 0);

            using var form = new DashboardForm(vm, ctx.LoggerFactory, isPinned: true, isPreviewMode: false);

            // CreateControl() alone gives the form a handle but child controls never paint —
            // their OnLoad/OnPaint cycles never fire without the message pump. The result is a
            // bitmap with only the form background. Show() offscreen + DoEvents() drains the
            // pending paint cycle for every child, so DrawToBitmap captures the full layout.
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(OffscreenX, OffscreenY);
            form.Show();
            try
            {
                Application.DoEvents();
                form.PerformLayout();

                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));

                Directory.CreateDirectory(Path.GetDirectoryName(ctx.OutputPath)!);
                bmp.Save(ctx.OutputPath, ImageFormat.Png);

                return new RenderResult(true, null, form.Width, form.Height);
            }
            finally
            {
                form.Hide();
            }
        }
        catch (Exception ex)
        {
            return new RenderResult(false, ex.Message, 0, 0);
        }
    }

    private static string? ValidateFixture(DashboardViewModel vm)
    {
        if (string.IsNullOrEmpty(vm.SessionId))
            return "fixture invalid: sessionId is required";
        if (string.IsNullOrEmpty(vm.SessionName))
            return "fixture invalid: sessionName is required";
        if (vm.Project is null)
            return "fixture invalid: project is required";
        if (vm.CwdPath is null)
            return "fixture invalid: cwdPath is required";
        if (string.IsNullOrEmpty(vm.Status))
            return "fixture invalid: status is required";

        if (vm.RecentTools is null)
            return "fixture invalid: recentTools is required";
        for (var i = 0; i < vm.RecentTools.Count; i++)
        {
            if (string.IsNullOrEmpty(vm.RecentTools[i].ToolName))
                return $"fixture invalid: recentTools[{i}].toolName is required";
        }

        if (vm.FleetItems is null)
            return "fixture invalid: fleetItems is required";
        for (var i = 0; i < vm.FleetItems.Count; i++)
        {
            if (string.IsNullOrEmpty(vm.FleetItems[i].SessionId))
                return $"fixture invalid: fleetItems[{i}].sessionId is required";
            if (string.IsNullOrEmpty(vm.FleetItems[i].Status))
                return $"fixture invalid: fleetItems[{i}].status is required";
        }

        if (vm.Git is not null && string.IsNullOrEmpty(vm.Git.Branch))
            return "fixture invalid: git.branch is required when git is non-null";

        return null;
    }
}

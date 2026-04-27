using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Imrdy.Core.Diagnostics;
using Imrdy.Core.Hooks;
using Imrdy.Windows.Dashboard;
using Imrdy.Windows.Models;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Diagnostics;

/// <summary>
/// Handles the <c>render-live</c> IPC verb. Builds a <see cref="DashboardForm"/> for the
/// requested session offscreen, captures it via <c>DrawToBitmap</c>, and writes the PNG
/// atomically via a temp-file + <see cref="File.Move"/> swap.
/// Must be called on the UI thread (enforced by <see cref="InspectIpcServer"/> via
/// <c>BeginInvoke</c> dispatch).
/// </summary>
internal static class RenderLiveHandler
{
    /// <summary>
    /// Builds the render-live response for the given request.
    /// </summary>
    /// <param name="req">The incoming IPC request.</param>
    /// <param name="allSessions">Live snapshot of all tracked sessions.</param>
    /// <param name="store">Hook accumulation store for turn counts, tool names, etc.</param>
    /// <param name="gitCache">Git info cache owned by TrayApp.</param>
    /// <param name="loggerFactory">Used to construct the DashboardForm.</param>
    public static InspectResponse Handle(
        InspectRequest req,
        IReadOnlyList<SessionEntry> allSessions,
        HookAccumulationStore store,
        GitInfoCache gitCache,
        ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrEmpty(req.SessionId))
            return Error("session id is required");

        if (string.IsNullOrEmpty(req.OutputPath))
            return Error("output path is required");

        // Normalize before the rooted check so paths containing .. segments (e.g. C:\a\..\b)
        // are resolved to their canonical form before any filesystem operation.
        var normalizedOutput = Path.GetFullPath(req.OutputPath);
        if (!Path.IsPathRooted(normalizedOutput))
            return Error("output path must be absolute");

        var entry = allSessions.FirstOrDefault(s => s.SessionId == req.SessionId);
        if (entry is null)
            return Error("session not found");

        var cachedGit = gitCache.TryGetCached(entry.State.Cwd);
        var vm = LiveDashboardVmBuilder.BuildForSession(entry, store, cachedGit, allSessions, DateTimeOffset.UtcNow);

        using var form = new DashboardForm(vm, loggerFactory, isPinned: true, isPreviewMode: false);
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.Show();
        try
        {
            Application.DoEvents();
            form.PerformLayout();

            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));

            var tempPath = normalizedOutput + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(normalizedOutput)!);
            try
            {
                bmp.Save(tempPath, ImageFormat.Png);
                File.Move(tempPath, normalizedOutput, overwrite: true);
            }
            catch
            {
                // Clean up the .tmp orphan — the target path is safe (File.Move has not run)
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                throw;
            }

            return new InspectResponse("1", "render-live", null,
                new RenderResult(form.Width, form.Height, normalizedOutput), null);
        }
        finally
        {
            form.Hide(); // 'using' disposes form; do NOT call form.Dispose() here
        }
    }

    private static InspectResponse Error(string message) =>
        new InspectResponse("1", "render-live", message, null, null);
}

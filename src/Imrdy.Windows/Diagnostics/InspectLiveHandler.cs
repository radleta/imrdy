using System.Windows.Forms;
using Imrdy.Core.Diagnostics;
using Imrdy.Core.Hooks;
using Imrdy.Windows.Dashboard;
using Imrdy.Windows.Models;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Diagnostics;

/// <summary>
/// Handles the <c>inspect-live</c> IPC verb. Builds a <see cref="DashboardForm"/> for the
/// requested session offscreen, walks its control tree, runs the layout analyzer, and returns
/// a structured <see cref="InspectResponse"/>. Must be called on the UI thread (enforced by
/// <see cref="InspectIpcServer"/> via <c>BeginInvoke</c> dispatch).
/// </summary>
internal static class InspectLiveHandler
{
    private const int DashboardRegionRadius = 14;

    /// <summary>
    /// Builds the inspect-live response for the given request.
    /// </summary>
    /// <param name="req">The incoming IPC request.</param>
    /// <param name="allSessions">Live snapshot of all tracked sessions.</param>
    /// <param name="store">Hook accumulation store for turn counts, tool names, etc.</param>
    /// <param name="gitCache">Git info cache owned by TrayApp (D5 ownership promotion).</param>
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

        var entry = allSessions.FirstOrDefault(s => s.SessionId == req.SessionId);
        if (entry is null)
            return Error("session not found");

        var cachedGit = gitCache.TryGetCached(entry.State.Cwd);
        var vm = LiveDashboardVmBuilder.BuildForSession(entry, store, cachedGit, allSessions, DateTimeOffset.UtcNow);

        DashboardForm? form = null;
        try
        {
            form = new DashboardForm(vm, loggerFactory, isPinned: true, isPreviewMode: false);

            // Render offscreen — same pattern as DashboardRenderer.
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-32000, -32000);
            form.Show();
            Application.DoEvents();
            form.PerformLayout();

            var (geom, tree) = InspectService.Walk(form, DashboardRegionRadius);
            var diagnostics = LayoutAnalyzer.Analyze(geom, tree);

            var result = new InspectResult(
                Form: geom,
                Tree: tree,
                Diagnostics: diagnostics,
                DiagnosticTimestamp: DateTimeOffset.UtcNow.ToString("O"));

            return new InspectResponse("1", "inspect-live", null, null, result);
        }
        finally
        {
            if (form is not null && !form.IsDisposed)
            {
                form.Hide();
                form.Dispose();
            }
        }
    }

    private static InspectResponse Error(string message) =>
        new InspectResponse("1", "inspect-live", message, null, null);
}

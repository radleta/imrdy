using System.Windows.Forms;
using Imrdy.Core.Desktop;
using Imrdy.Core.Display;
using Imrdy.Core.Workspace;
using Imrdy.Windows.Overlay;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Dashboard;

/// <summary>
/// Workspace-domain hover dashboard controller. Derives from
/// <see cref="HoverDashboardControllerBase"/> and provides workspace-specific:
/// hit-testing (filters by <see cref="DisplayItemType.Workspace"/>),
/// view-model building (<see cref="WorkspaceDashboardViewModelBuilder.Build"/>),
/// and form creation (<see cref="WorkspaceDashboardForm"/>).
///
/// Overrides <see cref="HoverDashboardControllerBase.OnSameItemRefreshTick"/> so that
/// <see cref="WorkspaceDashboardViewModel.ActivityText"/> advances every ~1s while the
/// dashboard is visible.
///
/// Fade animation (+0.5/-0.5 per tick, 200ms per direction) is driven by the
/// base <see cref="HoverDashboardControllerBase.OnDrainTick"/> call from TrayApp.
/// </summary>
internal sealed class WorkspaceHoverDashboardController : HoverDashboardControllerBase
{
    private readonly WorkspaceStore _workspaceStore;
    private readonly Func<string, DateTimeOffset?> _getWorkspaceLastSeenAt;
    private readonly Func<int?> _getCurrentDesktopIndex;
    private readonly GitInfoCache _gitCache;

    public WorkspaceHoverDashboardController(
        OverlayPanel overlayWindow,
        IDesktopManager? desktopManager,
        ILoggerFactory loggerFactory,
        WorkspaceStore workspaceStore,
        Func<string, DateTimeOffset?> getWorkspaceLastSeenAt,
        Func<int?> getCurrentDesktopIndex,
        GitInfoCache gitCache)
        : base(overlayWindow, desktopManager, loggerFactory)
    {
        _workspaceStore          = workspaceStore;
        _getWorkspaceLastSeenAt  = getWorkspaceLastSeenAt;
        _getCurrentDesktopIndex  = getCurrentDesktopIndex;
        _gitCache                = gitCache;
        _logger.LogDebug(
            "WorkspaceHoverCtrl: ctor complete overlay-window-ref={Handle}",
            overlayWindow.Handle);
    }

    // ---- Abstract overrides ----

    protected override bool TryHitTestForOurDomain(int clientX, out DisplayItem? item, out int hitIndex)
    {
        if (!_overlayWindow.TryHitTestAtClient(clientX, out item, out hitIndex))
            return false;

        if (item is null || item.ItemType != DisplayItemType.Workspace)
        {
            item     = null;
            hitIndex = -1;
            return false;
        }

        return true;
    }

    protected override object? BuildViewModel(DisplayItem item)
    {
        var entry = _workspaceStore.Load().Workspaces
            .FirstOrDefault(w => w.Path.Equals(item.Id, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            _logger.LogDebug(
                "WorkspaceHoverCtrl: BuildViewModel — no WorkspaceEntry for {WorkspacePath}; suppressing show",
                item.Id);
            return null;
        }

        var lastSeen = _getWorkspaceLastSeenAt(entry.Path);
        var current  = _getCurrentDesktopIndex();
        var git      = _gitCache.TryGetCached(entry.Path);

        return WorkspaceDashboardViewModelBuilder.Build(entry, git, current, lastSeen, DateTimeOffset.UtcNow);
    }

    protected override Form CreateForm(object viewModel)
        => new WorkspaceDashboardForm(
            (WorkspaceDashboardViewModel)viewModel,
            _desktopManager,
            _loggerFactory);

    protected override void ShowForm(HoverDashboardFormBase form, object viewModel)
        => ((WorkspaceDashboardForm)form).Show((WorkspaceDashboardViewModel)viewModel);

    protected override void ApplyViewModelUpdate(HoverDashboardFormBase form, object viewModel)
        => ((WorkspaceDashboardForm)form).Update((WorkspaceDashboardViewModel)viewModel);

    // ---- Extension-point override ----

    /// <summary>
    /// Rebuilds the view model with a fresh <see cref="DateTimeOffset.UtcNow"/> snapshot
    /// and applies it so the "active X ago" text advances every ~1s while the form is visible.
    /// </summary>
    protected override void OnSameItemRefreshTick(DisplayItem currentItem)
    {
        _logger.LogDebug("WorkspaceHoverCtrl: throttled-refresh workspacePath={Path}", currentItem.Id);
        var vm = BuildViewModel(currentItem);
        if (vm is not null)
            UpdateCurrentForm(vm);
    }
}

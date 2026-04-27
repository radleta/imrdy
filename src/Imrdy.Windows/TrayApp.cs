using System.Collections.Concurrent;
using Imrdy.Core;
using Imrdy.Core.Desktop;
using Imrdy.Core.Diagnostics;
using Imrdy.Core.Display;
using Imrdy.Core.Graphics;
using Imrdy.Core.Hooks;
using Imrdy.Core.Icons;
using Imrdy.Core.Menus;
using Imrdy.Core.Sound;
using Imrdy.Core.State;
using Imrdy.Core.Status;
using Imrdy.Core.Tooltip;
using Imrdy.Core.Workspace;
using Imrdy.Windows.Dashboard;
using Imrdy.Windows.Desktop;
using Imrdy.Windows.Diagnostics;
using Imrdy.Windows.Icons;
using Imrdy.Windows.Interaction;
using Imrdy.Windows.Menus;
using Imrdy.Windows.Models;
using Imrdy.Windows.Notifications;
using Imrdy.Windows.Overlay;
using Imrdy.Windows.Sound;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Imrdy.Windows;

/// <summary>
/// WinForms ApplicationContext that manages the system tray monitor.
/// Owns FileSystemWatchers, debounce/sweep/stale timers, and session/workspace lifecycle.
/// </summary>
internal sealed class TrayApp : ApplicationContext, ISessionInteractionRouter
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TeammatePresenceTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TeammateQuietThreshold = TimeSpan.FromSeconds(15);

    private readonly ILogger _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly StateFileReader _stateReader;
    private readonly WorkspaceStore _workspaceStore;
    private readonly CooldownTracker _cooldownTracker;
    private readonly NotificationDwellState _dwellState;
    private readonly PackLoader _packLoader;
    private readonly IDesktopManager _desktopManager;
    private readonly MonitorOptions _options;
    private readonly Dictionary<string, ITrayIconRenderer> _rendererCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TrayIconRendererFactory _rendererFactory;
    private readonly GraphicsPackLoader _graphicsPackLoader;
    private string _currentIconStyle;
    private OverlayWindowBase? _overlayWindow;
    private bool _overlayEnabled;
    private OverlayConfig _overlayConfig = new();
    private bool _trayEnabled = true;
    private readonly BalloonTipManager _balloonTipManager;
    private readonly WorkspaceVisibility _workspaceVisibility = new();
    private readonly WinFormsSoundPlayer _soundPlayer = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private InspectIpcServer? _inspectIpcServer;
    private readonly Dictionary<string, ShuffleBag<string>> _soundBags = new();

    private bool _soundEnabled = true;
    private SoundConfig _soundConfig = new();
    private IReadOnlyList<PackLoader.LoadedPack> _loadedPacks = [];

    private NotifyIcon _controllerIcon = null!;

    private readonly Dictionary<string, SessionEntry> _sessions = new();
    private readonly Dictionary<string, WorkspaceSessionEntry> _workspaces = new();

    private readonly ConcurrentQueue<string> _pendingChanges = new();

    private FileSystemWatcher? _sessionWatcher;
    private FileSystemWatcher? _workspaceWatcher;
    private FileSystemWatcher? _configWatcher;

    private System.Windows.Forms.Timer? _drainTimer;
    private System.Windows.Forms.Timer? _sweepTimer;
    private System.Windows.Forms.Timer? _staleTimer;
    private System.Windows.Forms.Timer? _agingTimer;

    private volatile bool _disposed;

    // Dev-build preview-dashboard processes launched from Manage → Dev menu.
    // Tracked so the same menu's Close-All item can kill unreachable preview windows
    // without the user dropping to a PowerShell one-liner. Prod builds never populate.
    private readonly List<System.Diagnostics.Process> _previewProcesses = new();
    private readonly object _previewProcessesLock = new();

    private readonly HookAccumulationStore _hookAccumulationStore;
    private readonly GitInfoCache _gitCache;
    private HoverDashboardController? _hoverController;
    // F6: one-shot null-controller warning guard — prevents log spam when controller is absent.
    // Reset to false whenever _hoverController becomes non-null so each absence is reported once.
    private bool _loggedNullHoverControllerWarning;
    // Typed reference to the overlay when it is an InteractiveOverlayWindow.
    // Null when the overlay is passive or disabled.
    private InteractiveOverlayWindow? _interactiveOverlayWindow;

    /// <summary>
    /// True during initial sweep — suppresses toasts and sounds until first sweep completes.
    /// Checked by icon/sound/notification logic in Steps 9+.
    /// </summary>
    public bool IsBootstrapping { get; private set; } = true;

    public TrayApp(
        ILoggerFactory loggerFactory,
        StateFileReader stateReader,
        WorkspaceStore workspaceStore,
        CooldownTracker cooldownTracker,
        NotificationDwellState dwellState,
        PackLoader packLoader,
        IDesktopManager desktopManager,
        MonitorOptions options,
        TrayIconRendererFactory rendererFactory,
        GraphicsPackLoader graphicsPackLoader)
    {
        _logger = loggerFactory.CreateLogger<TrayApp>();
        _loggerFactory = loggerFactory;
        _stateReader = stateReader;
        _workspaceStore = workspaceStore;
        _cooldownTracker = cooldownTracker;
        _dwellState = dwellState;
        _packLoader = packLoader;
        _desktopManager = desktopManager;
        _options = options;
        _rendererFactory = rendererFactory;
        _graphicsPackLoader = graphicsPackLoader;
        _hookAccumulationStore = new HookAccumulationStore();
        _gitCache = new GitInfoCache(loggerFactory);
        _currentIconStyle = StyleNames.NormalizeStyleName(ConfigReader.Read().Tray.IconStyle) ?? "circles";
        _balloonTipManager = new BalloonTipManager(loggerFactory)
        {
            Disabled = _options.NoToast,
            OnToastClicked = OnToastClicked,
        };

        // --silent overrides config
        if (_options.Silent)
        {
            _soundEnabled = false;
        }

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Application.ApplicationExit += OnApplicationExit;
        SetConsoleCtrlHandler(OnConsoleCtrl, true);
        ListenForStopSignal();

        _controllerIcon = new NotifyIcon
        {
            Icon = new Icon(typeof(TrayApp), "Resources.imrdy.ico"),
            Text = "imrdy",
            Visible = true,
            ContextMenuStrip = ControllerMenuBuilder.Create(
                GetControllerState,
                OnConfigChanged,
                sessionId => ActivateSession(sessionId),
                workspacePath => ActivateWorkspace(workspacePath),
                () => ExitThread(),
                LaunchPreview,
                CloseAllPreviews,
                _logger),
        };

        // UI marshaler: force the controller menu's native window handle to exist
        // on the current (UI) thread so BeginInvoke from background threads (toast
        // activation, etc.) works even when the user never opens the menu. Without
        // this, Control.BeginInvoke silently fails for overlay-only users whose
        // ContextMenuStrip is never shown.
        _ = _controllerIcon.ContextMenuStrip!.Handle;

        StartIpcServer();

        InitializeDirectories();
        LoadSoundConfig();
        InitializeWatchers();
        InitializeTimers();

        var startupConfig = ConfigReader.Read();
        _trayEnabled = startupConfig.Tray.Enabled;
        var overlayConfig = startupConfig.Overlay;
        _overlayEnabled = overlayConfig.Enabled;
        _overlayConfig = overlayConfig;
        if (_overlayEnabled)
        {
            try
            {
                _overlayWindow = CreateOverlay(overlayConfig);
                _interactiveOverlayWindow = _overlayWindow as InteractiveOverlayWindow;
                _overlayWindow.Show();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create overlay window — overlay disabled");
                _overlayWindow = null;
                _interactiveOverlayWindow = null;
                _overlayEnabled = false;
            }
        }

        if (_interactiveOverlayWindow is not null)
        {
            _hoverController = new HoverDashboardController(
                _interactiveOverlayWindow,
                _hookAccumulationStore,
                () => _sessions.Values.ToList(),
                _desktopManager,
                _loggerFactory,
                _gitCache);
            _loggedNullHoverControllerWarning = false; // F6: reset so next absence is reported
            _interactiveOverlayWindow.SurfaceInteracted += _hoverController.HandleSurfaceInteraction;
            _logger.LogDebug("TrayApp: subscribed _hoverController.HandleSurfaceInteraction to _interactiveOverlayWindow.SurfaceInteracted");
        }

        // Initial load to pick up existing sessions
        BootstrapSessions();
        IsBootstrapping = false;

        _logger.LogInformation("TrayApp started — monitoring {Dir} (NotifyIcon reflection: {Avail})",
            ImrdyPaths.Sessions, NotifyIconMenuHost.ReflectionAvailable);
    }

    private void InitializeDirectories()
    {
        Directory.CreateDirectory(ImrdyPaths.Sessions);
    }

    private void InitializeWatchers()
    {
        // Watch session state files
        _sessionWatcher = new FileSystemWatcher(ImrdyPaths.Sessions, "*.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 65536, // 64KB — default 8KB can overflow with rapid writes
            EnableRaisingEvents = true,
        };
        _sessionWatcher.Changed += OnSessionFileChanged;
        _sessionWatcher.Created += OnSessionFileChanged;
        _sessionWatcher.Deleted += OnSessionFileDeleted;
        _sessionWatcher.Error += (_, err) =>
            _logger.LogWarning(err.GetException(), "Session FileSystemWatcher error");

        // Watch workspaces.json
        _workspaceWatcher = new FileSystemWatcher(ImrdyPaths.Home, "workspaces.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _workspaceWatcher.Changed += OnWorkspaceFileChanged;
        _workspaceWatcher.Created += OnWorkspaceFileChanged;

        // Watch config.json for live reload (Created+Changed only — NOT Deleted)
        Directory.CreateDirectory(ImrdyPaths.Home);
        _configWatcher = new FileSystemWatcher(ImrdyPaths.Home, "config.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };
        _configWatcher.Changed += OnSoundConfigFileChanged;
        _configWatcher.Created += OnSoundConfigFileChanged;
        // NO _configWatcher.Deleted subscription — atomic write briefly deletes the file
    }

    private void InitializeTimers()
    {
        // Drain timer: processes FSW events on UI thread (100ms)
        _drainTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _drainTimer.Tick += OnDrainTimerTick;
        _drainTimer.Tick += OnHoverDrainTick;
        _drainTimer.Start();

        // Cleanup timer: removes sessions whose state files are gone (10s)
        _sweepTimer = new System.Windows.Forms.Timer { Interval = 10_000 };
        _sweepTimer.Tick += OnCleanupTimerTick;
        _sweepTimer.Start();

        // Stale timer: removes old sessions (60s)
        _staleTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
        _staleTimer.Tick += OnStaleTimerTick;
        _staleTimer.Start();

        // Aging timer: fades icons over time (5s)
        _agingTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _agingTimer.Tick += OnAgingTimerTick;
        _agingTimer.Start();
    }

    // --- Sound Config ---

    private void LoadSoundConfig()
    {
        try
        {
            var config = ConfigReader.Read();
            _soundConfig = config.Sound;
            _soundEnabled = _options.Silent ? false : _soundConfig.Enabled;
            _loadedPacks = _packLoader.LoadPacks(ImrdyPaths.PacksDir);
            _soundBags.Clear();
            _balloonTipManager.SuppressSystemSound = _loadedPacks.Count > 0 && _soundEnabled;
            _logger.LogDebug("Sound config loaded: enabled={Enabled}, packs={Count}",
                _soundEnabled, _loadedPacks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load sound config");
        }
    }

    // --- FSW Callbacks (background thread → queue) ---

    private void OnSessionFileChanged(object sender, FileSystemEventArgs e)
    {
        _logger.LogDebug("FSW: {ChangeType} {Path}", e.ChangeType, e.Name);
        _pendingChanges.Enqueue(e.FullPath);
    }

    private void OnSessionFileDeleted(object sender, FileSystemEventArgs e)
    {
        _pendingChanges.Enqueue($"DELETE:{e.FullPath}");
    }

    private void OnWorkspaceFileChanged(object sender, FileSystemEventArgs e)
    {
        _pendingChanges.Enqueue("WORKSPACE_RELOAD");
    }

    private void OnSoundConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        _pendingChanges.Enqueue("SOUND_CONFIG_RELOAD");
    }

    // --- Timer Callbacks (UI thread) ---

    private void OnDrainTimerTick(object? sender, EventArgs e)
    {
        try
        {
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (_pendingChanges.TryDequeue(out var item))
            {
                if (!processed.Add(item))
                {
                    continue; // Debounce: skip duplicate events in same drain cycle
                }

                if (item == "SOUND_CONFIG_RELOAD")
                {
                    LoadSoundConfig();
                }
                else if (item == "WORKSPACE_RELOAD")
                {
                    ReloadWorkspaces();
                }
                else if (item.StartsWith("DELETE:", StringComparison.Ordinal))
                {
                    var path = item[7..];
                    HandleSessionFileDeleted(path);
                }
                else
                {
                    HandleSessionFileChanged(item);
                }
            }

            // Dwell notification dispatch
            var now = DateTimeOffset.UtcNow;
            var fired = _dwellState.GetFiredSessions(now);
            foreach (var notification in fired)
            {
                try
                {
                    if (_sessions.TryGetValue(notification.SessionId, out var firedEntry))
                    {
                        _logger.LogInformation("Dwell fired for {SessionId}: {PreviousStatus} → {Status} (type={NotificationType})",
                            notification.SessionId, notification.PreviousStatus, notification.Status, notification.NotificationType ?? "status-change");

                        // Update icon to the settled status (consensus promotion defers icon to here)
                        if (firedEntry.State.Status != notification.Status)
                        {
                            firedEntry.State = firedEntry.State with { Status = notification.Status };
                            UpdateSessionIcon(firedEntry);
                        }

                        // Toast: status-change entries + idle_prompt (the authoritative "genuinely idle" signal)
                        if (notification.NotificationType is null
                            || string.Equals(notification.NotificationType, "idle_prompt", StringComparison.OrdinalIgnoreCase))
                        {
                            _balloonTipManager.OnStatusTransition(
                                firedEntry, notification.PreviousStatus, notification.Status,
                                IsBootstrapping, _desktopManager.GetCurrentDesktopIndex());
                        }

                        // Sound: dispatch via correct path based on entry origin
                        if (notification.NotificationType is not null)
                            TriggerNotificationSound(firedEntry, notification.NotificationType!); // null-forgiving: guarded by is not null check; avoids CS8604 with TreatWarningsAsErrors
                        else
                            TriggerStatusChangeSound(firedEntry, notification.PreviousStatus, notification.Status);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Dwell dispatch failed for {SessionId}", notification.SessionId);
                }
            }

            // Consensus promotion: if lead is "done" and all teammates are quiet (no activity
            // for TeammateQuietThreshold), promote to idle (green) + toast/sound.
            foreach (var (sessionId, entry) in _sessions)
            {
                if (entry.State.Status != "done")
                    continue;

                if (entry.ConsensusPromoted)
                    continue; // Already promoted this "done" cycle

                if (entry.State.LastTeammateAt is null)
                    continue; // No teammates — normal dwell path handles this

                if (now - entry.State.LastTeammateAt < TeammateQuietThreshold)
                    continue; // Teammates still active

                // All teammates quiet + lead done → promote to idle.
                // Icon deferred to dwell fire (5s settle prevents green/red toggling during rapid tool calls).
                entry.ConsensusPromoted = true;
                _logger.LogInformation("Consensus promotion for {SessionId}: all teammates quiet for {Quiet}s",
                    sessionId, (int)(now - entry.State.LastTeammateAt.Value).TotalSeconds);

                _dwellState.OnStatusChanged(sessionId, "idle", "done", now);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in drain timer");
        }
    }

    private void OnHoverDrainTick(object? sender, EventArgs e)
    {
        if (_hoverController is null)
        {
            // F6: log once when the tick fires but controller is absent (overlay disabled or
            // between config-change races). Guard prevents repeat spam.
            if (!_loggedNullHoverControllerWarning)
            {
                _loggedNullHoverControllerWarning = true;
                _logger.LogDebug("TrayApp: OnHoverDrainTick fired but _hoverController is null (overlay disabled or between config-change races)");
            }
            return;
        }
        _hoverController.OnDrainTick(DateTimeOffset.UtcNow);
    }

    private void OnCleanupTimerTick(object? sender, EventArgs e)
    {
        try
        {
            CleanupGoneSessions();

            // Update controller tooltip with session count
            var count = _sessions.Count;
            _controllerIcon.Text = count > 0 ? $"imrdy \u2014 {count} sessions" : "imrdy";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in cleanup timer");
        }
    }

    private void OnStaleTimerTick(object? sender, EventArgs e)
    {
        try
        {
            CleanupStaleSessions();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in stale timer");
        }
    }

    private void OnAgingTimerTick(object? sender, EventArgs e)
    {
        try
        {
            foreach (var (_, entry) in _sessions)
            {
                if (entry.Icon is null) continue;

                var agingSince = DateTimeOffset.UtcNow - entry.LastSeenAt;
                var newTier = StatusMap.GetAgingTier(agingSince);

                // Only update icon if aging tier changed (avoid GDI churn)
                if (newTier != entry.LastAgingTier)
                {
                    entry.LastAgingTier = newTier;
                    entry.Icon.Icon = GetRendererForStyle(ResolveSessionIconStyle(entry)).GetIcon(entry.State.Status, newTier);
                }

                // Always update tooltip (status age changes every tick)
                entry.Icon.Text = FormatSessionTooltip(entry);
            }

            RefreshOverlay();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in aging timer");
        }
    }

    // --- Session Lifecycle ---

    private void HandleSessionFileChanged(string filePath)
    {
        var state = _stateReader.ReadStateFile(filePath);
        if (state is null)
        {
            return;
        }

        if (_sessions.TryGetValue(state.SessionId, out var entry))
        {
            // Skip re-processing when sweep re-reads an unchanged state file
            if (entry.LastProcessedTimestamp == state.Timestamp)
            {
                return;
            }

            entry.LastProcessedTimestamp = state.Timestamp;

            // idle_prompt is a 60s backstop that fires even when subagents are still active.
            // When teammates are present, keep the session at "done" — consensus handles promotion.
            var hasActiveTeammates = state.LastTeammateAt is not null
                && DateTimeOffset.UtcNow - state.LastTeammateAt < TeammatePresenceTimeout;

            if (hasActiveTeammates
                && state.Status == "idle"
                && string.Equals(state.NotificationType, "idle_prompt", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Suppressed idle_prompt for {SessionId}: teammates still active, keeping done",
                    state.SessionId);
                state = state with { Status = "done", NotificationType = "" };
            }

            var previousStatus = entry.State.Status;
            var statusChanged = previousStatus != state.Status;
            var previousNotificationType = entry.State.NotificationType;
            var notificationChanged = previousNotificationType != state.NotificationType;
            entry.State = state;
            entry.DesktopIndex = state.DesktopIndex;

            if (statusChanged)
            {
                entry.StatusSince = DateTimeOffset.UtcNow;
                entry.ConsensusPromoted = false;

                // Auto-restore dismissed sessions on attention-worthy status changes
                if (entry.Dismissed && state.Status is "busy" or "attention" or "permission" or "error")
                {
                    entry.Dismissed = false;
                    if (entry.Icon is not null)
                    {
                        entry.Icon.Visible = true;
                    }
                    _logger.LogInformation("Restored dismissed session: {SessionId} (status → {Status})",
                        entry.SessionId, state.Status);
                }

                if (!IsBootstrapping)
                {
                    // Suppress dwell entry for "done" status when teammates are active.
                    // The consensus check in OnDrainTimerTick handles promotion once all teammates are quiet.
                    if (state.Status == "done" && hasActiveTeammates)
                    {
                        _logger.LogDebug("Suppressed dwell for {SessionId}: done with active teammates", entry.SessionId);
                    }
                    else
                    {
                        _dwellState.OnStatusChanged(entry.SessionId, state.Status, previousStatus, DateTimeOffset.UtcNow);
                    }
                }
            }

            // Notification-type sounds (only when notification type actually changes)
            if (notificationChanged && !IsBootstrapping && !string.IsNullOrEmpty(state.NotificationType))
            {
                // Map notification type to status for dwell duration lookup
                var mappedStatus = state.NotificationType switch
                {
                    "permission_prompt" or "elicitation_dialog" => "permission",
                    "idle_prompt" => "idle",
                    _ => (string?)null,
                };
                if (mappedStatus is not null)
                {
                    _dwellState.OnStatusChanged(entry.SessionId, mappedStatus, entry.State.Status,
                        DateTimeOffset.UtcNow, notificationType: state.NotificationType);
                }
            }

            // SessionEnd → start grace period (only once; sweep re-reads must not reset it)
            if (string.Equals(state.HookEvent, "SessionEnd", StringComparison.OrdinalIgnoreCase))
            {
                entry.RemoveAfter ??= DateTimeOffset.UtcNow + GracePeriod;
            }
            else
            {
                entry.RemoveAfter = null;
            }

            UpdateSessionIcon(entry);
            var latencyMs = (int)(DateTimeOffset.UtcNow - state.Timestamp).TotalMilliseconds;
            _logger.LogInformation("Session {SessionId} → {Status} (latency: {Latency}ms)", state.SessionId, state.Status, latencyMs);
        }
        else
        {
            // New session — resolve pack from config/project rules, then persist
            var resolvedPack = state.SoundPack;
            if (string.IsNullOrEmpty(resolvedPack))
            {
                var assignment = new PackAssignment(_loadedPacks, _soundConfig);
                resolvedPack = assignment.Resolve(null, PathNormalizer.DeriveProject(state.Cwd));
            }

            // Map null (no pack resolved) to "" (explicitly none) so it persists
            resolvedPack ??= "";

            // Normalize and validate icon style — reject unknown values (fall back to null = global default)
            var normalizedStyle = StyleNames.NormalizeStyleName(state.IconStyle);
            if (normalizedStyle is not null
                && !StyleNames.BuiltInStyles.Contains(normalizedStyle, StringComparer.OrdinalIgnoreCase)
                && !normalizedStyle.StartsWith("pack:", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Unknown icon style '{Style}' in state file for {SessionId} — ignoring", normalizedStyle, state.SessionId);
                normalizedStyle = null;
            }

            entry = new SessionEntry
            {
                SessionId = state.SessionId,
                State = state,
                SoundPack = resolvedPack,
                DesktopIndex = state.DesktopIndex,
                IconStyle = normalizedStyle,
                LastProcessedTimestamp = state.Timestamp,
                StartedAt = state.StartedAt ?? state.Timestamp,
            };

            // Write resolved pack and icon style to state file so the hook preserves them
            PersistSessionSoundPack(entry);
            PersistSessionIconStyle(entry);

            // Session first observed as SessionEnd (tray started mid-session, or hook fired
            // before FSW picked up earlier writes) — start grace period so cleanup removes it.
            // Without this, FSW won't fire again and CleanupGoneSessions never sets RemoveAfter,
            // so the stale state file lingers forever.
            if (string.Equals(state.HookEvent, "SessionEnd", StringComparison.OrdinalIgnoreCase))
            {
                entry.RemoveAfter = DateTimeOffset.UtcNow + GracePeriod;
            }

            _sessions[state.SessionId] = entry;
            CreateSessionIcon(entry);

            // Toast for new session (suppressed during bootstrap)
            _balloonTipManager.OnNewSession(entry, IsBootstrapping);

            _logger.LogInformation("Session started: {SessionId} ({Project})",
                state.SessionId, state.Project);
        }

        // Feed the hook event into the accumulation store so the hover dashboard
        // sees live sparkline/chip/turn-count data. Runs synchronously on the UI thread
        // (same FSW callback). BootstrapSessions intentionally does NOT call Apply —
        // accumulators start empty and refill organically from new events.
        if (!IsBootstrapping && entry is not null)
        {
            var evt = new HookEventModel
            {
                HookEventName = state.HookEvent,
                SessionId = state.SessionId,
                ToolName = state.ToolName,
                NotificationType = state.NotificationType,
            };
            _hookAccumulationStore.Apply(evt, derivedStatus: entry.State.Status);
        }

        // Update workspace visibility after any session change (D11)
        UpdateWorkspaceVisibility();
    }

    private void HandleSessionFileDeleted(string filePath)
    {
        var sessionId = Path.GetFileNameWithoutExtension(filePath);
        if (_sessions.TryGetValue(sessionId, out var entry))
        {
            entry.RemoveAfter = DateTimeOffset.UtcNow + GracePeriod;
            _logger.LogDebug("Session file deleted, grace period started: {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// One-time startup load: reads all existing state files and creates session entries.
    /// Called once during initialization before FSW takes over real-time updates.
    /// </summary>
    private void BootstrapSessions()
    {
        if (!Directory.Exists(ImrdyPaths.Sessions))
        {
            return;
        }

        // Load workspaces BEFORE sessions so ResolveSessionIconStyle has workspace data
        ReloadWorkspaces();

        var stateFiles = _stateReader.ReadAllStateFiles(ImrdyPaths.Sessions);
        foreach (var state in stateFiles)
        {
            HandleSessionFileChanged(Path.Combine(ImrdyPaths.Sessions, $"{state.SessionId}.json"));
        }
    }

    /// <summary>
    /// Periodic cleanup: removes sessions whose state files no longer exist on disk.
    /// Defense-in-depth for FSW Deleted events that may be missed.
    /// Does NOT re-read state files — FSW handles all real-time updates.
    /// </summary>
    private void CleanupGoneSessions()
    {
        var toRemove = new List<string>();
        var now = DateTimeOffset.UtcNow;

        foreach (var (sessionId, entry) in _sessions)
        {
            var stateFile = Path.Combine(ImrdyPaths.Sessions, $"{sessionId}.json");
            if (!File.Exists(stateFile))
            {
                if (entry.RemoveAfter is null)
                {
                    entry.RemoveAfter = now + GracePeriod;
                    _logger.LogDebug("State file gone, grace period started: {SessionId}", sessionId);
                }

                if (now >= entry.RemoveAfter)
                {
                    toRemove.Add(sessionId);
                }
            }

            // Also remove sessions that have passed their grace period (e.g. SessionEnd)
            if (entry.RemoveAfter.HasValue && now >= entry.RemoveAfter.Value)
            {
                toRemove.Add(sessionId);
            }
        }

        foreach (var sessionId in toRemove.Distinct())
        {
            RemoveSession(sessionId);
        }
    }

    private void CleanupStaleSessions()
    {
        var now = DateTimeOffset.UtcNow;
        var toRemove = new List<string>();

        foreach (var (sessionId, entry) in _sessions)
        {
            if (now - entry.State.Timestamp > TimeSpan.FromMinutes(_options.StaleMinutes))
            {
                // Keep session alive if its state file still exists on disk
                var stateFile = Path.Combine(ImrdyPaths.Sessions, $"{sessionId}.json");
                if (File.Exists(stateFile))
                {
                    continue;
                }

                toRemove.Add(sessionId);
                _logger.LogInformation("Removing stale session: {SessionId} (last update {Ago} ago)",
                    sessionId, now - entry.State.Timestamp);
            }
        }

        foreach (var sessionId in toRemove)
        {
            RemoveSession(sessionId);
        }
    }

    private void RemoveSession(string sessionId)
    {
        if (_sessions.Remove(sessionId, out var entry))
        {
            Commands.ProcessResolver.ClearSession(sessionId);
            _dwellState.RemoveSession(sessionId);
            _cooldownTracker.RemoveSession(sessionId);
            entry.Dispose();
            RefreshOverlay();

            // Delete state file so sweep doesn't resurrect the session
            var statePath = Path.Combine(ImrdyPaths.Sessions, $"{sessionId}.json");
            try
            {
                File.Delete(statePath);
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Could not delete state file: {Path}", statePath);
            }

            _logger.LogDebug("Session removed: {SessionId}", sessionId);

            // Workspace white dot may reappear now (D11)
            UpdateWorkspaceVisibility();
        }
    }

    /// <summary>
    /// Writes the session's sound_pack to its state file so the hook preserves it.
    /// </summary>
    private void PersistSessionSoundPack(SessionEntry entry)
    {
        PersistSessionField(entry, current => current with { SoundPack = entry.SoundPack });
    }

    /// <summary>
    /// Writes the session's icon_style to its state file so the hook preserves it.
    /// </summary>
    private void PersistSessionIconStyle(SessionEntry entry)
    {
        PersistSessionField(entry, current => current with { IconStyle = entry.IconStyle });
    }

    private void PersistSessionField(SessionEntry entry, Func<StateFileModel, StateFileModel> update)
    {
        var statePath = Path.Combine(ImrdyPaths.Sessions, $"{entry.SessionId}.json");
        try
        {
            var current = _stateReader.ReadStateFile(statePath);
            if (current is not null)
            {
                _stateReader.WriteStateFile(statePath, update(current));
            }
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Could not persist session field for {SessionId}", entry.SessionId);
        }
    }

    private void ClearAllSessions()
    {
        var sessionIds = _sessions.Keys.ToList();
        foreach (var sessionId in sessionIds)
        {
            var stateFile = Path.Combine(ImrdyPaths.Sessions, $"{sessionId}.json");
            try { if (File.Exists(stateFile)) File.Delete(stateFile); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete state file for {SessionId}", sessionId); }
            RemoveSession(sessionId);
        }
        _logger.LogInformation("Cleared all sessions ({Count} removed)", sessionIds.Count);
    }

    private void DumpState()
    {
        var lines = new List<string>();
        var ts = DateTimeOffset.UtcNow.ToString("HH:mm:ss.fff");
        lines.Add($"{ts} [DUMP] ===== State Dump ({_sessions.Count} sessions) =====");
        foreach (var (sid, entry) in _sessions)
        {
            var d = entry.State;
            var age = (int)(DateTimeOffset.UtcNow - entry.StatusSince).TotalSeconds;
            var seenAge = (int)(DateTimeOffset.UtcNow - entry.LastSeenAt).TotalSeconds;
            var agingTier = entry.LastAgingTier;
            lines.Add($"{ts} [DUMP] {sid[..Math.Min(8, sid.Length)]} project={d.Project} status={d.Status} hook={d.HookEvent} desktop={entry.DesktopIndex} pack={entry.SoundPack} dismissed={entry.Dismissed} statusAge={age}s seenAge={seenAge}s agingTier={agingTier} pid={d.ClaudePid}");

            // Check state file consistency
            var stateFile = Path.Combine(ImrdyPaths.Sessions, $"{sid}.json");
            if (File.Exists(stateFile))
            {
                try
                {
                    var fileState = _stateReader.ReadStateFile(stateFile);
                    if (fileState is not null && fileState.Status != d.Status)
                        lines.Add($"{ts} [DUMP] {sid[..Math.Min(8, sid.Length)]}   FILE MISMATCH: file.status={fileState.Status} vs memory.status={d.Status}");
                }
                catch (Exception ex)
                {
                    lines.Add($"{ts} [DUMP] {sid[..Math.Min(8, sid.Length)]}   FILE READ ERROR: {ex.Message}");
                }
            }
            else
            {
                lines.Add($"{ts} [DUMP] {sid[..Math.Min(8, sid.Length)]}   NO STATE FILE");
            }
        }
        lines.Add($"{ts} [DUMP] ===== End Dump =====");

        try
        {
            File.AppendAllText(ImrdyPaths.MonitorLog, string.Join(Environment.NewLine, lines) + Environment.NewLine);
            _logger.LogInformation("State dumped to {LogPath} ({Count} sessions)", ImrdyPaths.MonitorLog, _sessions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write state dump to {LogPath}", ImrdyPaths.MonitorLog);
        }
    }

    // --- Workspace Lifecycle ---

    private void ReloadWorkspaces()
    {
        try
        {
            var config = _workspaceStore.Load();
            var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var workspace in config.Workspaces)
            {
                var key = workspace.Path.ToUpperInvariant();
                activeKeys.Add(key);

                if (_workspaces.TryGetValue(key, out var existing))
                {
                    existing.Workspace = workspace;
                    existing.IconStyle = StyleNames.NormalizeStyleName(workspace.IconStyle);
                }
                else
                {
                    var wsEntry = new WorkspaceSessionEntry
                    {
                        Workspace = workspace,
                    };
                    wsEntry.IconStyle = StyleNames.NormalizeStyleName(workspace.IconStyle);
                    _workspaces[key] = wsEntry;
                    CreateWorkspaceIcon(wsEntry);
                    _logger.LogDebug("Workspace added: {Name} ({Path})", workspace.Name, workspace.Path);
                }
            }

            // Remove workspaces no longer in config
            var toRemove = _workspaces.Keys.Where(k => !activeKeys.Contains(k)).ToList();
            foreach (var key in toRemove)
            {
                if (_workspaces.Remove(key, out var wsEntry))
                {
                    wsEntry.Dispose();
                    _logger.LogDebug("Workspace removed: {Name}", wsEntry.Workspace.Name);
                }
            }
            UpdateWorkspaceVisibility();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reloading workspaces");
        }
    }

    /// <summary>
    /// Evaluates workspace visibility based on active sessions.
    /// White dots hide when a session is active in that workspace path (D11).
    /// Desktop auto-tracked from latest session; persisted on hidden→visible transition (D22).
    /// </summary>
    private void UpdateWorkspaceVisibility()
    {
        if (_workspaces.Count == 0)
        {
            return;
        }

        var workspaceEntries = _workspaces.Values.Select(ws => ws.Workspace).ToList();
        var activeSessions = _sessions.Values
            .Where(s => !s.Dismissed && s.State.Status != "end")
            .Select(s => s.State)
            .ToList();

        var results = _workspaceVisibility.Evaluate(workspaceEntries, activeSessions);

        foreach (var result in results)
        {
            var key = result.Workspace.Path.ToUpperInvariant();
            if (!_workspaces.TryGetValue(key, out var wsEntry))
            {
                continue;
            }

            var wasVisible = wsEntry.Visible;
            wsEntry.Visible = result.IsVisible;

            if (wsEntry.Icon is not null)
            {
                wsEntry.Icon.Visible = _trayEnabled && result.IsVisible;
            }

            // Persist desktop on hidden→visible transition (D22)
            if (result.DesktopChanged)
            {
                wsEntry.Workspace = wsEntry.Workspace with { Desktop = result.TrackedDesktop };
                try
                {
                    _workspaceStore.Pin(result.Workspace.Path, result.Workspace.Name, result.TrackedDesktop);
                    _logger.LogDebug("Workspace {Name} desktop updated to {Desktop} on reappear",
                        result.Workspace.Name, result.TrackedDesktop);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist workspace desktop for {Name}", result.Workspace.Name);
                }
            }
        }
        RefreshOverlay();
    }

    // --- Icon Management ---

    private ITrayIconRenderer GetRendererForStyle(string styleName)
    {
        if (_rendererCache.TryGetValue(styleName, out var cached))
            return cached;
        var renderer = _rendererFactory.Create(styleName);
        _rendererCache[styleName] = renderer;
        return renderer;
    }

    /// <summary>
    /// Resolves the effective icon style for a session: session override → workspace override → global default.
    /// </summary>
    private string ResolveSessionIconStyle(SessionEntry entry)
    {
        var sessionStyle = StyleNames.NormalizeStyleName(entry.IconStyle);
        if (sessionStyle is not null)
        {
            _logger.LogDebug("IconStyle for {SessionId}: session override → {Style}", entry.SessionId, sessionStyle);
            return sessionStyle;
        }

        // Inherit from workspace if session has no override
        var cwd = entry.State?.Cwd;
        if (!string.IsNullOrEmpty(cwd))
        {
            var key = PathNormalizer.Normalize(cwd).ToUpperInvariant();
            if (_workspaces.TryGetValue(key, out var ws))
            {
                var wsStyle = StyleNames.NormalizeStyleName(ws.IconStyle);
                if (wsStyle is not null)
                {
                    _logger.LogDebug("IconStyle for {SessionId}: workspace override → {Style}", entry.SessionId, wsStyle);
                    return wsStyle;
                }
            }
        }

        _logger.LogDebug("IconStyle for {SessionId}: global default → {Style}", entry.SessionId, _currentIconStyle);
        return _currentIconStyle;
    }

    private void CreateSessionIcon(SessionEntry entry)
    {
        var icon = GetRendererForStyle(ResolveSessionIconStyle(entry)).GetIcon(entry.State.Status, 0);

        entry.Icon = new NotifyIcon
        {
            Visible = _trayEnabled,
            Icon = icon,
            Text = FormatSessionTooltip(entry),
        };

        entry.Menu = SessionMenuBuilder.Create(
            entry,
            new SessionMenuCallbacks(
                OnSwitchDesktop: () => ActivateSession(entry.SessionId),
                OnAssignDesktop: () =>
                {
                    var currentDesktop = _desktopManager.GetCurrentDesktopIndex();
                    if (currentDesktop.HasValue)
                    {
                        entry.DesktopIndex = currentDesktop.Value;
                        _logger.LogDebug("Assigned session {SessionId} to desktop {Desktop}", entry.SessionId, currentDesktop.Value);
                    }
                },
                OnSetDesktop: index =>
                {
                    entry.DesktopIndex = index;
                    if (index.HasValue)
                    {
                        _desktopManager.SwitchToDesktop(index.Value);
                    }
                },
                OnSetPack: packName =>
                {
                    entry.SoundPack = packName ?? "";
                    PersistSessionSoundPack(entry);
                    _logger.LogDebug("Set session {SessionId} sound pack to {Pack}", entry.SessionId, entry.SoundPack);
                },
                OnSetIconStyle: styleName =>
                {
                    entry.IconStyle = styleName;
                    PersistSessionIconStyle(entry);
                    UpdateSessionIcon(entry);
                },
                OnPinWorkspace: () =>
                {
                    var cwd = entry.State.Cwd;
                    if (string.IsNullOrEmpty(cwd)) return;
                    var name = entry.State.Project ?? Path.GetFileName(cwd) ?? cwd;
                    var desktop = entry.DesktopIndex ?? _desktopManager.GetCurrentDesktopIndex() ?? 0;
                    try
                    {
                        _workspaceStore.Pin(cwd, name, desktop);
                        ReloadWorkspaces();
                        _logger.LogInformation("Pinned workspace from session: {Name} ({Path}) desktop={Desktop}",
                            name, cwd, desktop);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to pin workspace for session {SessionId}", entry.SessionId);
                    }
                },
                OnUnpinWorkspace: () =>
                {
                    var cwd = entry.State.Cwd;
                    if (string.IsNullOrEmpty(cwd)) return;
                    try
                    {
                        _workspaceStore.Unpin(cwd);
                        ReloadWorkspaces();
                        _logger.LogInformation("Unpinned workspace from session: {Path}", cwd);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to unpin workspace for session {SessionId}", entry.SessionId);
                    }
                },
                OnClear: () =>
                {
                    var stateFile = Path.Combine(ImrdyPaths.Sessions, $"{entry.SessionId}.json");
                    try { if (File.Exists(stateFile)) File.Delete(stateFile); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete state file for {SessionId}", entry.SessionId); }
                    RemoveSession(entry.SessionId);
                },
                OnClearAll: () => ClearAllSessions(),
                OnDumpState: () => DumpState(),
                OnExit: () => ExitThread()),
            getInstalledPacks: () => _loadedPacks.Select(p => p.Name).ToList(),
            getInstalledGraphicsPacks: () => _graphicsPackLoader.LoadPacks(ImrdyPaths.GraphicsPacksDir).Select(p => p.Name).ToList(),
            getDesktopCount: () => _desktopManager.GetDesktopCount(),
            getDesktopAvailable: () => _desktopManager.IsAvailable,
            getIsPinned: () => !string.IsNullOrEmpty(entry.State.Cwd) && _workspaceStore.IsPinned(entry.State.Cwd),
            logger: _logger);
        entry.Icon.ContextMenuStrip = entry.Menu;

        entry.Icon.MouseClick += (_, e) =>
        {
            // Route through the same interface as overlay clicks — age reset and icon
            // refresh live inside the router, not duplicated per surface.
            if (e.Button == MouseButtons.Left)
                ActivateSession(entry.SessionId);
            else if (e.Button == MouseButtons.Right)
                OpenSessionMenu(entry.SessionId, MenuAnchor.AtTrayIcon(entry.Icon));
        };

        RefreshOverlay();
    }

    /// <summary>
    /// Called from the toast activation background thread — marshals to UI thread
    /// and routes through the interaction router for age-reset parity with every
    /// other surface. The controller menu's handle is force-created in the ctor
    /// so <c>BeginInvoke</c> works even when the user never opens the menu (see
    /// "UI marshaler" in the ctor).
    /// </summary>
    private void OnToastClicked(string sessionId)
    {
        if (_disposed) return;

        if (_controllerIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            try
            {
                _controllerIcon.ContextMenuStrip.BeginInvoke(() => OnToastClicked(sessionId));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            return;
        }

        ActivateSession(sessionId);
    }

    private void SwitchToSessionDesktop(SessionEntry entry)
    {
        try
        {
            // Walk process tree from Claude PID to find the terminal window (cached per session)
            if (entry.State.ClaudePid is int claudePid)
            {
                var terminalPid = Commands.ProcessResolver.ResolveTerminalPid(claudePid, entry.SessionId);
                _logger.LogInformation("Focus: session={Sid} claudePid={Claude} terminalPid={Terminal}",
                    entry.SessionId[..8], claudePid, terminalPid);

                if (terminalPid.HasValue)
                {
                    try
                    {
                        var proc = System.Diagnostics.Process.GetProcessById(terminalPid.Value);
                        using (proc)
                        {
                            var hwnd = proc.MainWindowHandle;
                            _logger.LogInformation("Focus: terminal={Name}({Pid}) hwnd={Hwnd}",
                                proc.ProcessName, terminalPid.Value, hwnd);

                            if (hwnd != IntPtr.Zero)
                            {
                                var result = PInvokeWindow.ForceForeground(hwnd);
                                _logger.LogInformation("Focus: ForceForeground result={Result}", result);
                                if (result)
                                {
                                    return;
                                }
                                // ForceForeground fails from balloon-tip notification context
                                // (Windows blocks SetForegroundWindow). Fall through to desktop
                                // switch so the user at least lands on the right desktop.
                            }
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Terminal process died — clear cache so next click re-walks
                        Commands.ProcessResolver.ClearSession(entry.SessionId);
                        _logger.LogWarning("Focus: terminal process {Pid} died, cache cleared", terminalPid.Value);
                    }
                }
            }
            else
            {
                _logger.LogInformation("Focus: session={Sid} has no ClaudePid", entry.SessionId[..8]);
            }

            // Fallback: switch to desktop by stored index
            if (_desktopManager.IsAvailable && entry.DesktopIndex.HasValue)
            {
                _desktopManager.SwitchToDesktop(entry.DesktopIndex.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to switch to desktop for session {SessionId}",
                entry.SessionId);
        }
    }

    private void SwitchToWorkspaceDesktop(WorkspaceSessionEntry entry)
    {
        try
        {
            if (_desktopManager.IsAvailable)
            {
                _desktopManager.SwitchToDesktop(entry.Workspace.Desktop);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to switch to desktop for workspace {Name}",
                entry.Workspace.Name);
        }
    }

    // --- ISessionInteractionRouter ---
    //
    // Single entry point for every user-initiated session/workspace interaction.
    // Every public method here follows the same two-phase shape:
    //   1. Mark interaction (reset age, refresh icon — uniform across all surfaces)
    //   2. Dispatch the intent-specific action (switch desktop / show menu)
    //
    // Callers must NOT call SwitchToSessionDesktop / SwitchToWorkspaceDesktop /
    // menu.Show / NotifyIconMenuHost.Show directly from event handlers.

    public void ActivateSession(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry))
        {
            _logger.LogDebug("ActivateSession: unknown session {SessionId}", sessionId);
            return;
        }
        MarkSessionInteracted(entry);
        SwitchToSessionDesktop(entry);
    }

    public void ActivateWorkspace(string workspacePath)
    {
        // _workspaces is keyed by path.ToUpperInvariant() (see workspace load logic)
        var key = workspacePath.ToUpperInvariant();
        if (!_workspaces.TryGetValue(key, out var entry))
        {
            _logger.LogDebug("ActivateWorkspace: unknown workspace {Path}", workspacePath);
            return;
        }
        MarkWorkspaceInteracted(entry);
        SwitchToWorkspaceDesktop(entry);
    }

    public void OpenSessionMenu(string sessionId, MenuAnchor anchor)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry))
        {
            _logger.LogDebug("OpenSessionMenu: unknown session {SessionId}", sessionId);
            return;
        }
        MarkSessionInteracted(entry);
        ShowContextMenuAt(entry.Menu, anchor);
    }

    public void OpenWorkspaceMenu(string workspacePath, MenuAnchor anchor)
    {
        var key = workspacePath.ToUpperInvariant();
        if (!_workspaces.TryGetValue(key, out var entry))
        {
            _logger.LogDebug("OpenWorkspaceMenu: unknown workspace {Path}", workspacePath);
            return;
        }
        MarkWorkspaceInteracted(entry);
        ShowContextMenuAt(entry.Menu, anchor);
    }

    // Dispatches a menu to the right mechanism for the anchor:
    //   - Tray NotifyIcon (shell-delivered click): NotifyIconMenuHost (reflects the
    //     NotifyIcon's private ShowContextMenu via AttachThreadInput).
    //   - Control owner (overlay form click): vanilla menu.Show(owner, location).
    // Callers always use MenuAnchor.AtTrayIcon / MenuAnchor.AtControl, so exactly one
    // branch fires.
    private static void ShowContextMenuAt(ContextMenuStrip? menu, MenuAnchor anchor)
    {
        if (menu is null) return;
        if (anchor.TrayIcon is { } icon)
            NotifyIconMenuHost.Show(icon);
        else if (anchor.Owner is { } owner)
            menu.Show(owner, anchor.Location);
    }

    // Resets interaction age and refreshes icon/tooltip/overlay to brightest tier.
    // Shared by every ActivateSession / OpenSessionMenu path.
    private void MarkSessionInteracted(SessionEntry entry)
    {
        entry.LastSeenAt = DateTimeOffset.UtcNow;
        UpdateSessionIcon(entry);
    }

    private void MarkWorkspaceInteracted(WorkspaceSessionEntry entry)
    {
        // Workspace icons always render at tier 0 (never dim), so no icon refresh.
        entry.LastSeenAt = DateTimeOffset.UtcNow;
    }

    private void UpdateSessionIcon(SessionEntry entry)
    {
        if (entry.Icon is null)
        {
            return;
        }

        var agingSince = DateTimeOffset.UtcNow - entry.LastSeenAt;
        var tier = StatusMap.GetAgingTier(agingSince);
        entry.LastAgingTier = tier;
        entry.Icon.Icon = GetRendererForStyle(ResolveSessionIconStyle(entry)).GetIcon(entry.State.Status, tier);
        entry.Icon.Text = FormatSessionTooltip(entry);
        RefreshOverlay();
    }

    private void CreateWorkspaceIcon(WorkspaceSessionEntry entry)
    {
        var icon = GetRendererForStyle(StyleNames.NormalizeStyleName(entry.IconStyle) ?? _currentIconStyle).GetIcon("workspace", 0);

        entry.Icon = new NotifyIcon
        {
            Visible = _trayEnabled,
            Icon = icon,
            Text = TooltipFormatter.FormatWorkspace(entry.Workspace.Name, entry.Workspace.Desktop),
        };

        entry.Menu = WorkspaceMenuBuilder.Create(
            entry,
            onUnpin: path =>
            {
                _workspaceStore.Unpin(path);
                ReloadWorkspaces();
            },
            onAssignDesktop: () =>
            {
                var currentDesktop = _desktopManager.GetCurrentDesktopIndex();
                if (currentDesktop.HasValue)
                {
                    _workspaceStore.SetDesktop(entry.Workspace.Path, currentDesktop.Value);
                    ReloadWorkspaces();
                }
            },
            onSetDesktop: desktop =>
            {
                _workspaceStore.SetDesktop(entry.Workspace.Path, desktop);
                ReloadWorkspaces();
            },
            getDesktopCount: () => _desktopManager.GetDesktopCount(),
            getDesktopAvailable: () => _desktopManager.IsAvailable,
            onSetIconStyle: styleName =>
            {
                entry.IconStyle = styleName;
                _workspaceStore.SetIconStyle(entry.Workspace.Path, styleName);
                if (entry.Icon is not null)
                    entry.Icon.Icon = GetRendererForStyle(
                        StyleNames.NormalizeStyleName(entry.IconStyle) ?? _currentIconStyle)
                        .GetIcon("workspace", 0);
                // Refresh sessions that inherit from this workspace
                RefreshAllSessionIcons();
            },
            getInstalledGraphicsPacks: () => _graphicsPackLoader.LoadPacks(ImrdyPaths.GraphicsPacksDir).Select(p => p.Name).ToList(),
            logger: _logger);
        entry.Icon.ContextMenuStrip = entry.Menu;

        entry.Icon.MouseClick += (_, e) =>
        {
            // Route through the same interface as overlay clicks.
            if (e.Button == MouseButtons.Left)
                ActivateWorkspace(entry.Workspace.Path);
            else if (e.Button == MouseButtons.Right)
                OpenWorkspaceMenu(entry.Workspace.Path, MenuAnchor.AtTrayIcon(entry.Icon));
        };
    }

    private static string FormatSessionTooltip(SessionEntry entry)
    {
        var age = DateTimeOffset.UtcNow - entry.StatusSince;
        return TooltipFormatter.FormatSession(
            entry.State.Project,
            entry.State.SessionName,
            entry.State.Status,
            age,
            entry.DesktopIndex,
            entry.SoundPack);
    }

    // --- Controller State ---

    private ControllerMenuState GetControllerState()
    {
        return new ControllerMenuState
        {
            Sessions = _sessions.Values.Select(e => new SessionMenuState
            {
                SessionId = e.SessionId,
                Status = e.State.Status,
                Project = e.State.Project,
                DesktopIndex = e.DesktopIndex,
            }).ToList(),
            Workspaces = _workspaces.Values.Select(w => new WorkspaceMenuState
            {
                WorkspaceName = w.Workspace.Name,
                WorkspacePath = w.Workspace.Path,
                DesktopIndex = w.Workspace.Desktop,
            }).ToList(),
            InstalledPacks = _loadedPacks.Select(p => p.Name).ToList(),
            InstalledGraphicsPacks = _graphicsPackLoader.LoadPacks(ImrdyPaths.GraphicsPacksDir)
                .Select(p => p.Name).ToList(),
            Config = ConfigReader.Read(),
            LogPath = ImrdyPaths.MonitorLog,
            DevBuild = BuildDevState(),
        };
    }

    // --- Dev-build fixtures & preview-dashboard processes ---

    private DevBuildState? BuildDevState()
    {
        if (!File.Exists(ImrdyPaths.DevBuildMarker))
            return null;

        var fixtures = new List<DevFixture>();
        try
        {
            var repoPath = File.ReadAllText(ImrdyPaths.DevBuildMarker).Trim();
            if (!string.IsNullOrEmpty(repoPath) && Directory.Exists(repoPath))
            {
                var dir = Path.Combine(repoPath, "tests", "fixtures", "dashboards");
                if (Directory.Exists(dir))
                {
                    foreach (var path in Directory.EnumerateFiles(dir, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                        fixtures.Add(new DevFixture(Path.GetFileNameWithoutExtension(path), path));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enumerate dev fixtures");
        }

        return new DevBuildState
        {
            Fixtures = fixtures,
            RunningPreviewCount = CountAndPruneAlivePreviews(),
        };
    }

    private void LaunchPreview(string fixturePath)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                _logger.LogWarning("Cannot launch preview: Environment.ProcessPath is null");
                return;
            }
            var psi = new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = false,
            };
            psi.ArgumentList.Add("preview-dashboard");
            psi.ArgumentList.Add(fixturePath);
            var proc = System.Diagnostics.Process.Start(psi);
            if (proc is not null)
            {
                lock (_previewProcessesLock)
                    _previewProcesses.Add(proc);
                _logger.LogInformation("Launched preview: pid={Pid} fixture={Fixture}", proc.Id, fixturePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to launch preview for {Path}", fixturePath);
        }
    }

    private void CloseAllPreviews()
    {
        List<System.Diagnostics.Process> snapshot;
        lock (_previewProcessesLock)
        {
            snapshot = new List<System.Diagnostics.Process>(_previewProcesses);
            _previewProcesses.Clear();
        }
        var killed = 0;
        foreach (var proc in snapshot)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    killed++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill preview pid={Pid}", SafePid(proc));
            }
            finally
            {
                proc.Dispose();
            }
        }
        _logger.LogInformation("Closed all previews: killed={Killed}, tracked={Tracked}", killed, snapshot.Count);
    }

    private int CountAndPruneAlivePreviews()
    {
        lock (_previewProcessesLock)
        {
            for (var i = _previewProcesses.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (_previewProcesses[i].HasExited)
                    {
                        _previewProcesses[i].Dispose();
                        _previewProcesses.RemoveAt(i);
                    }
                }
                catch
                {
                    _previewProcesses.RemoveAt(i);
                }
            }
            return _previewProcesses.Count;
        }
    }

    private static int SafePid(System.Diagnostics.Process proc)
    {
        try { return proc.Id; }
        catch { return -1; }
    }

    private void OnConfigChanged(ImrdyConfig config)
    {
        _soundConfig = config.Sound;
        _soundEnabled = _options.Silent ? false : config.Sound.Enabled;
        _loadedPacks = _packLoader.LoadPacks(ImrdyPaths.PacksDir);
        _soundBags.Clear();
        _balloonTipManager.SuppressSystemSound = _loadedPacks.Count > 0 && _soundEnabled;
        _logger.LogDebug("Config updated from controller menu: enabled={Enabled}, default={Default}",
            config.Sound.Enabled, config.Sound.DefaultPack);

        var newIconStyle = StyleNames.NormalizeStyleName(config.Tray.IconStyle) ?? "circles";
        if (!string.Equals(newIconStyle, _currentIconStyle, StringComparison.OrdinalIgnoreCase))
        {
            _currentIconStyle = newIconStyle;
            RefreshAllSessionIcons();
            var overlayForInvalidate = _overlayWindow;
            if (overlayForInvalidate is not null)
                overlayForInvalidate.BeginInvoke(() => overlayForInvalidate.InvalidateStyleCache());
            _logger.LogInformation("Icon style changed to {IconStyle}", newIconStyle);
        }

        var newTrayEnabled = config.Tray.Enabled;
        if (newTrayEnabled != _trayEnabled)
        {
            _trayEnabled = newTrayEnabled;
            ApplyTrayEnabledToAll();
            _logger.LogInformation("Tray icons {State}", _trayEnabled ? "enabled" : "disabled");
        }

        var overlayNowEnabled = config.Overlay.Enabled;
        if (config.Overlay != _overlayConfig || overlayNowEnabled != _overlayEnabled)
        {
            // Any overlay config change — including Interactive toggling — recreates the
            // window. Interactive is class-level now (PassiveOverlayWindow vs
            // InteractiveOverlayWindow), so switching it means switching class.
            // Unsubscribe from the old overlay BEFORE disposing controller and window.
            if (_interactiveOverlayWindow is not null && _hoverController is not null)
            {
                _interactiveOverlayWindow.SurfaceInteracted -= _hoverController.HandleSurfaceInteraction;
                _logger.LogDebug("TrayApp: unsubscribed _hoverController.HandleSurfaceInteraction from _interactiveOverlayWindow.SurfaceInteracted");
            }
            _hoverController?.Dispose();
            _hoverController = null;
            _overlayWindow?.Dispose();
            _overlayWindow = null;
            _interactiveOverlayWindow = null;
            _overlayConfig = config.Overlay;

            if (overlayNowEnabled)
            {
                try
                {
                    _overlayWindow = CreateOverlay(config.Overlay);
                    _interactiveOverlayWindow = _overlayWindow as InteractiveOverlayWindow;
                    _overlayWindow.Show();
                    RefreshOverlay();
                    _logger.LogInformation("Overlay enabled with position={Position}, size={Size}, interactive={Interactive}",
                        config.Overlay.Position, config.Overlay.Size, config.Overlay.Interactive ?? true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create overlay window during config change");
                    _overlayWindow = null;
                    _interactiveOverlayWindow = null;
                    overlayNowEnabled = false;
                }

                if (_interactiveOverlayWindow is not null)
                {
                    _hoverController = new HoverDashboardController(
                        _interactiveOverlayWindow,
                        _hookAccumulationStore,
                        () => _sessions.Values.ToList(),
                        _desktopManager,
                        _loggerFactory,
                        _gitCache);
                    _loggedNullHoverControllerWarning = false; // F6: reset so next absence is reported
                    _interactiveOverlayWindow.SurfaceInteracted += _hoverController.HandleSurfaceInteraction;
                    _logger.LogDebug("TrayApp: subscribed _hoverController.HandleSurfaceInteraction to _interactiveOverlayWindow.SurfaceInteracted");
                }
            }
            else
            {
                _logger.LogInformation("Overlay disabled");
            }
            _overlayEnabled = overlayNowEnabled;
        }
    }

    private OverlayWindowBase CreateOverlay(OverlayConfig config)
    {
        var interactive = config.Interactive ?? true;
        return interactive
            ? new InteractiveOverlayWindow(config, this, _loggerFactory, _graphicsPackLoader)
            : new PassiveOverlayWindow(config, _loggerFactory, _graphicsPackLoader);
    }

    private void RefreshAllSessionIcons()
    {
        foreach (var entry in _sessions.Values)
        {
            if (entry.Icon is null) continue;
            var agingSince = DateTimeOffset.UtcNow - entry.LastSeenAt;
            var tier = StatusMap.GetAgingTier(agingSince);
            entry.LastAgingTier = tier;
            entry.Icon.Icon = GetRendererForStyle(ResolveSessionIconStyle(entry)).GetIcon(entry.State.Status, tier);
        }
        foreach (var ws in _workspaces.Values)
        {
            if (ws.Icon is null) continue;
            ws.Icon.Icon = GetRendererForStyle(StyleNames.NormalizeStyleName(ws.IconStyle) ?? _currentIconStyle).GetIcon("workspace", 0);
        }
    }

    private BuiltDisplayItems BuildDisplayItems()
    {
        // Pre-compute workspace visibility via the existing _workspaceVisibility instance (D11).
        // Do NOT create a second WorkspaceVisibility — that would split hidden→visible tracking state.
        var workspaceEntries = _workspaces.Values.Select(ws => ws.Workspace).ToList();
        var activeSessions = _sessions.Values
            .Where(s => !s.Dismissed && s.State.Status != "end")
            .Select(s => s.State)
            .ToList();
        var visibilityResults = _workspaceVisibility.Evaluate(workspaceEntries, activeSessions);
        var workspaceVisible = visibilityResults.ToDictionary(
            r => r.Workspace.Path.ToUpperInvariant(),
            r => r.IsVisible,
            StringComparer.OrdinalIgnoreCase);

        var inputs = new List<DisplayItemInput>(_sessions.Count + _workspaces.Count);

        foreach (var s in _sessions.Values)
        {
            if (s.Icon is null || s.State is null) continue;
            // Logical visibility — independent of the tray god toggle. The tray toggle
            // is applied separately by DisplayItemCollection.Build via the trayEnabled
            // parameter (ForTray is empty when off; ForOverlay is unaffected). Using
            // s.Icon.Visible here would cause tray-off to also blank the overlay,
            // because Step 8's ApplyTrayEnabledToAll ties Icon.Visible to _trayEnabled.
            var sessionVisible = !s.Dismissed
                && (s.RemoveAfter is null || s.RemoveAfter > DateTimeOffset.UtcNow)
                && s.State.Status != "end";
            inputs.Add(new DisplayItemInput(
                Id: s.SessionId,
                ItemType: DisplayItemType.Session,
                Status: s.State.Status,
                DesktopIndex: s.DesktopIndex,
                IconStyle: ResolveSessionIconStyle(s),
                AgingTier: s.LastAgingTier,
                IsVisible: sessionVisible,
                Label: s.State.Project ?? s.SessionId));
        }

        foreach (var ws in _workspaces.Values)
        {
            var key = ws.Workspace.Path.ToUpperInvariant();
            var isVisible = workspaceVisible.TryGetValue(key, out var v) ? v : ws.Visible;
            inputs.Add(new DisplayItemInput(
                Id: ws.Workspace.Path,
                ItemType: DisplayItemType.Workspace,
                Status: "workspace",
                DesktopIndex: ws.Workspace.Desktop,
                IconStyle: StyleNames.NormalizeStyleName(ws.IconStyle) ?? _currentIconStyle,
                AgingTier: 0,
                IsVisible: isVisible,
                Label: ws.Workspace.Name));
        }

        // Use cached _trayEnabled — NEVER call ConfigReader.Read() here (hot path; called every drain tick).
        return DisplayItemCollection.Build(inputs, _trayEnabled);
    }

    private void ApplyTrayEnabledToAll()
    {
        foreach (var entry in _sessions.Values)
        {
            if (entry.Icon is null) continue;
            // Off: all session icons hidden regardless of other state.
            // On: restore per the existing visibility rules — dismissed sessions stay hidden,
            // sessions pending removal stay hidden, all other sessions become visible.
            // Mirrors CreateSessionIcon's Visible=_trayEnabled default plus the explicit "not
            // dismissed and not past RemoveAfter" guard that governs steady-state visibility.
            // Unconditionally setting Visible = _trayEnabled would resurrect dismissed sessions
            // or those inside RemoveAfter grace (regression against sweep-removal fix 4702e86).
            var shouldShow = !entry.Dismissed
                && (entry.RemoveAfter is null || entry.RemoveAfter > DateTimeOffset.UtcNow);
            entry.Icon.Visible = _trayEnabled && shouldShow;
        }
        // Workspaces honor both the god toggle and the per-workspace visibility rule.
        // UpdateWorkspaceVisibility already reads _trayEnabled in the gated assignment.
        UpdateWorkspaceVisibility();
    }

    private void RefreshOverlay()
    {
        if (_overlayWindow is null || !_overlayEnabled) return;
        var items = BuildDisplayItems();
        _overlayWindow.UpdateItems(items.ForOverlay);
    }

    // --- Sound Triggers (port of PS1:700-750) ---

    private void TriggerStatusChangeSound(SessionEntry entry, string previousStatus, string newStatus)
    {
        if (IsBootstrapping)
        {
            return;
        }

        if (_cooldownTracker.IsOnCooldown(entry.SessionId, DateTimeOffset.UtcNow))
        {
            return;
        }

        // Map status transitions to sound events
        SoundEvent? soundEvent = (previousStatus, newStatus) switch
        {
            (_, "busy") when previousStatus != "busy" => SoundEvent.GettingToWork,
            (_, "error") when previousStatus != "error" => SoundEvent.NeedsYou,
            (_, "idle") when previousStatus is "busy" or "done" => SoundEvent.Finished,
            (_, "end") => SoundEvent.SessionEnd,
            _ => null,
        };

        // SessionStart is triggered by new session creation, not status change
        if (soundEvent is null)
        {
            return;
        }

        PlaySoundEvent(entry, soundEvent.Value);
    }

    private void TriggerNotificationSound(SessionEntry entry, string notificationType)
    {
        if (IsBootstrapping || string.IsNullOrEmpty(notificationType))
        {
            return;
        }

        if (_cooldownTracker.IsOnCooldown(entry.SessionId, DateTimeOffset.UtcNow))
        {
            return;
        }

        SoundEvent? soundEvent = notificationType switch
        {
            "permission_prompt" or "elicitation_dialog" => SoundEvent.NeedsYou,
            "idle_prompt" => SoundEvent.Forgotten,
            _ => null,
        };

        if (soundEvent is null)
        {
            return;
        }

        PlaySoundEvent(entry, soundEvent.Value);
    }

    private void PlaySoundEvent(SessionEntry entry, SoundEvent soundEvent)
    {
        if (!_soundEnabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var isCombo = _cooldownTracker.RecordAndCheckCombo(entry.SessionId, now);
        var effectiveEvent = isCombo ? SoundEvent.Combo : soundEvent;

        if (isCombo)
        {
            _logger.LogDebug("Combo detected for session {SessionId}, playing Combo instead of {Original}",
                entry.SessionId, soundEvent);
        }

        // Use the session's assigned pack — resolved at creation, sticky until changed
        var packName = entry.SoundPack;
        if (string.IsNullOrEmpty(packName))
        {
            return;
        }

        // Find the loaded pack
        var pack = _loadedPacks.FirstOrDefault(p =>
            string.Equals(p.Name, packName, StringComparison.OrdinalIgnoreCase));

        if (pack is null || !pack.WavFiles.TryGetValue(effectiveEvent, out var wavPaths) || wavPaths.Length == 0)
        {
            _logger.LogDebug("No WAV files for {Event} in pack {Pack}", effectiveEvent, packName);
            return;
        }

        // Get or create shuffle bag for this pack+event combination
        var bagKey = $"{packName}:{effectiveEvent}";
        if (!_soundBags.TryGetValue(bagKey, out var bag))
        {
            bag = new ShuffleBag<string>(wavPaths);
            _soundBags[bagKey] = bag;
        }

        var wavPath = bag.Draw();
        if (wavPath is null)
        {
            return;
        }

        _soundPlayer.Play(wavPath);
        _logger.LogDebug("Playing {Event} from {Pack}: {File}",
            effectiveEvent, packName, Path.GetFileName(wavPath));
    }

    // --- IPC server ---

    private void StartIpcServer()
    {
        var config = ConfigReader.Read();
        var ipcEnabled = config.Diagnostics.IpcEnabled ?? File.Exists(ImrdyPaths.DevBuildMarker);
        if (!ipcEnabled)
        {
            return;
        }

        var handlers = new Dictionary<string, Func<InspectRequest, InspectResponse>>(StringComparer.Ordinal)
        {
            ["inspect-live"] = req => InspectLiveHandler.Handle(
                req, _sessions.Values.ToList(), _hookAccumulationStore, _gitCache, _loggerFactory),
            ["render-live"] = req => RenderLiveHandler.Handle(
                req, _sessions.Values.ToList(), _hookAccumulationStore, _gitCache, _loggerFactory),
        };

        // Test-only: when IMRDY_TEST_HOLD_HANDLE is set, register a "ping" verb whose handler
        // blocks on the named system event until signalled. Used by StressTests.ConcurrentConnections
        // to hold 4 server slots open and verify the concurrency cap. Never populated in production
        // (the env var is never set outside test harnesses).
        var holdHandleName = Environment.GetEnvironmentVariable("IMRDY_TEST_HOLD_HANDLE");
        if (!string.IsNullOrEmpty(holdHandleName))
        {
            handlers["ping"] = _ =>
            {
                if (EventWaitHandle.TryOpenExisting(holdHandleName, out var ev))
                {
                    using (ev)
                        ev.WaitOne();
                }
                return new InspectResponse("1", "ping", null, null, null);
            };
        }

        _inspectIpcServer = new InspectIpcServer(_loggerFactory, _controllerIcon.ContextMenuStrip!, handlers);
        _inspectIpcServer.Start(_shutdownCts.Token);
    }

    // --- Stop signal ---

    private void ListenForStopSignal()
    {
        var stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ImrdyPaths.StopEventName);
        var token = _shutdownCts.Token;
        Task.Run(() =>
        {
            try
            {
                WaitHandle.WaitAny([stopEvent, token.WaitHandle]);
                if (!token.IsCancellationRequested)
                {
                    _logger.LogInformation("Stop signal received — exiting");
                    // Application.Exit() is thread-safe and posts WM_QUIT to the UI pump.
                    // It triggers Application.ApplicationExit → OnApplicationExit → Shutdown().
                    // Do NOT use ContextMenuStrip.BeginInvoke — that silently fails when the
                    // strip's native handle hasn't been created (i.e. the user has never
                    // right-clicked the tray to open the menu), leaving the tray unable to exit.
                    Application.Exit();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stop listener failed");
            }
            finally
            {
                stopEvent.Dispose();
            }
        }, token);
    }

    // --- Shutdown ---

    protected override void ExitThreadCore()
    {
        Shutdown();
        base.ExitThreadCore();
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        Shutdown();
    }

    private void OnApplicationExit(object? sender, EventArgs e)
    {
        Shutdown();
    }

    private void Shutdown()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logger.LogInformation("TrayApp shutting down");

        _shutdownCts.Cancel();

        // Stop timers
        _drainTimer?.Stop();
        _drainTimer?.Dispose();
        _sweepTimer?.Stop();
        _sweepTimer?.Dispose();
        _staleTimer?.Stop();
        _staleTimer?.Dispose();
        _agingTimer?.Stop();
        _agingTimer?.Dispose();


        // Stop watchers
        if (_sessionWatcher is not null)
        {
            _sessionWatcher.EnableRaisingEvents = false;
            _sessionWatcher.Dispose();
        }

        if (_workspaceWatcher is not null)
        {
            _workspaceWatcher.EnableRaisingEvents = false;
            _workspaceWatcher.Dispose();
        }

        if (_configWatcher is not null)
        {
            _configWatcher.EnableRaisingEvents = false;
            _configWatcher.Dispose();
        }

        // Dispose hover dashboard controller before overlay and session entries
        if (_interactiveOverlayWindow is not null && _hoverController is not null)
        {
            _interactiveOverlayWindow.SurfaceInteracted -= _hoverController.HandleSurfaceInteraction;
            _logger.LogDebug("TrayApp: unsubscribed _hoverController.HandleSurfaceInteraction from _interactiveOverlayWindow.SurfaceInteracted");
        }
        _hoverController?.Dispose();
        _hoverController = null;

        // Close any dev-build preview windows we launched, so they don't linger after tray exit.
        CloseAllPreviews();

        // Dispose controller icon
        _controllerIcon.Visible = false;
        _controllerIcon.Icon?.Dispose();
        _controllerIcon.ContextMenuStrip?.Dispose();
        _controllerIcon.Dispose();

        // Dispose overlay window
        _overlayWindow?.Dispose();
        _overlayWindow = null;
        _interactiveOverlayWindow = null;

        // Dispose all session icons
        foreach (var entry in _sessions.Values)
        {
            entry.Dispose();
        }

        _sessions.Clear();

        // Dispose all workspace icons
        foreach (var entry in _workspaces.Values)
        {
            entry.Dispose();
        }

        _workspaces.Clear();

        foreach (var r in _rendererCache.Values)
            r.Dispose();
        _rendererCache.Clear();
        _balloonTipManager.Dispose();
        _soundPlayer.Dispose();
        _desktopManager.Dispose();
        _inspectIpcServer?.Dispose();
        _shutdownCts.Dispose();

        _logger.LogInformation("TrayApp shutdown complete");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Shutdown();
        }

        base.Dispose(disposing);
    }

    // --- Console Ctrl Handler (catches Ctrl+C, console close, logoff, shutdown) ---

    private delegate bool ConsoleCtrlDelegate(int ctrlType);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? handler, bool add);

    // Must be a static field to prevent GC collection of the delegate
    private static readonly ConsoleCtrlDelegate OnConsoleCtrl = ctrlType =>
    {
        // ctrlType: 0=Ctrl+C, 1=Ctrl+Break, 2=Close, 5=Logoff, 6=Shutdown
        Application.Exit();
        return true;
    };
}

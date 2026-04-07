using System.Collections.Concurrent;
using Imrdy.Core;
using Imrdy.Core.Desktop;
using Imrdy.Core.Menus;
using Imrdy.Core.Sound;
using Imrdy.Core.State;
using Imrdy.Core.Status;
using Imrdy.Core.Tooltip;
using Imrdy.Core.Workspace;
using Imrdy.Windows.Desktop;
using Imrdy.Windows.Icons;
using Imrdy.Windows.Menus;
using Imrdy.Windows.Models;
using Imrdy.Windows.Notifications;
using Imrdy.Windows.Sound;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Imrdy.Windows;

/// <summary>
/// WinForms ApplicationContext that manages the system tray monitor.
/// Owns FileSystemWatchers, debounce/sweep/stale timers, and session/workspace lifecycle.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);

    private readonly ILogger _logger;
    private readonly StateFileReader _stateReader;
    private readonly WorkspaceStore _workspaceStore;
    private readonly CooldownTracker _cooldownTracker;
    private readonly PackLoader _packLoader;
    private readonly IDesktopManager _desktopManager;
    private readonly MonitorOptions _options;
    private readonly AgingCache _agingCache = new();
    private readonly BalloonTipManager _balloonTipManager;
    private readonly WorkspaceVisibility _workspaceVisibility = new();
    private readonly WinFormsSoundPlayer _soundPlayer = new();
    private readonly CancellationTokenSource _shutdownCts = new();
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

    private bool _disposed;

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
        PackLoader packLoader,
        IDesktopManager desktopManager,
        MonitorOptions options)
    {
        _logger = loggerFactory.CreateLogger<TrayApp>();
        _stateReader = stateReader;
        _workspaceStore = workspaceStore;
        _cooldownTracker = cooldownTracker;
        _packLoader = packLoader;
        _desktopManager = desktopManager;
        _options = options;
        _balloonTipManager = new BalloonTipManager(loggerFactory)
        {
            Disabled = _options.NoToast,
        };

        // --silent overrides config
        if (_options.Silent)
        {
            _soundEnabled = false;
        }

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Application.ApplicationExit += OnApplicationExit;
        SetConsoleCtrlHandler(OnConsoleCtrl, true);

        _controllerIcon = new NotifyIcon
        {
            Icon = new Icon(typeof(TrayApp), "Resources.imrdy.ico"),
            Text = "imrdy",
            Visible = true,
            ContextMenuStrip = ControllerMenuBuilder.Create(GetControllerState, OnConfigChanged, () => ExitThread(), _logger),
        };

        InitializeDirectories();
        LoadSoundConfig();
        InitializeWatchers();
        InitializeTimers();

        // Initial sweep to pick up existing sessions
        PerformSweep();
        IsBootstrapping = false;

        _logger.LogInformation("TrayApp started — monitoring {Dir}", ImrdyPaths.Sessions);
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
        _drainTimer.Start();

        // Sweep timer: catches missed FSW events (10s)
        _sweepTimer = new System.Windows.Forms.Timer { Interval = 10_000 };
        _sweepTimer.Tick += OnSweepTimerTick;
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
        _logger.LogInformation("FSW: {ChangeType} {Path}", e.ChangeType, e.Name);
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in drain timer");
        }
    }

    private void OnSweepTimerTick(object? sender, EventArgs e)
    {
        try
        {
            PerformSweep();

            // Update controller tooltip with session count
            var count = _sessions.Count;
            _controllerIcon.Text = count > 0 ? $"imrdy \u2014 {count} sessions" : "imrdy";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in sweep timer");
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
        foreach (var (_, entry) in _sessions)
        {
            if (entry.Icon is null) continue;

            var (r, g, b) = StatusMap.ResolveColor(entry.State.Status);
            var agingSince = DateTimeOffset.UtcNow - entry.LastSeenAt;
            var newFactor = CircleIconRenderer.GetAgingFactor(agingSince);

            // Only update icon if aging tier changed (avoid GDI churn)
            if (Math.Abs(newFactor - entry.LastAgingFactor) > 0.001)
            {
                entry.LastAgingFactor = newFactor;
                entry.Icon.Icon = _agingCache.GetOrCreate(r, g, b, newFactor);
            }

            // Always update tooltip (status age changes every tick)
            entry.Icon.Text = FormatSessionTooltip(entry);
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
            var previousStatus = entry.State.Status;
            var statusChanged = previousStatus != state.Status;
            entry.State = state;
            entry.SoundPack = state.SoundPack;
            entry.DesktopIndex = state.DesktopIndex;

            if (statusChanged)
            {
                entry.StatusSince = DateTimeOffset.UtcNow;

                // Auto-restore dismissed sessions on attention-worthy status changes
                if (entry.Dismissed && state.Status is "busy" or "attention" or "permission")
                {
                    entry.Dismissed = false;
                    if (entry.Icon is not null)
                    {
                        entry.Icon.Visible = true;
                    }
                    _logger.LogInformation("Restored dismissed session: {SessionId} (status → {Status})",
                        entry.SessionId, state.Status);
                }

                _balloonTipManager.OnStatusTransition(
                    entry, previousStatus, state.Status,
                    IsBootstrapping, currentDesktopIndex: _desktopManager.GetCurrentDesktopIndex());

                // Sound trigger on status change
                TriggerStatusChangeSound(entry, previousStatus, state.Status);
            }

            // Notification-type sounds (every write, not just status change)
            TriggerNotificationSound(entry, state.NotificationType);

            // SessionEnd → start grace period
            if (string.Equals(state.HookEvent, "SessionEnd", StringComparison.OrdinalIgnoreCase))
            {
                entry.RemoveAfter = DateTimeOffset.UtcNow + GracePeriod;
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
            // New session
            entry = new SessionEntry
            {
                SessionId = state.SessionId,
                State = state,
                SoundPack = state.SoundPack,
                DesktopIndex = state.DesktopIndex,
            };

            _sessions[state.SessionId] = entry;
            CreateSessionIcon(entry);

            // Toast for new session (suppressed during bootstrap)
            _balloonTipManager.OnNewSession(entry, IsBootstrapping);

            _logger.LogInformation("Session started: {SessionId} ({Project})",
                state.SessionId, state.Project);
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

    private void PerformSweep()
    {
        if (!Directory.Exists(ImrdyPaths.Sessions))
        {
            return;
        }

        var stateFiles = _stateReader.ReadAllStateFiles(ImrdyPaths.Sessions);
        var activeIds = new HashSet<string>();

        foreach (var state in stateFiles)
        {
            activeIds.Add(state.SessionId);
            HandleSessionFileChanged(Path.Combine(ImrdyPaths.Sessions, $"{state.SessionId}.json"));
        }

        // Remove sessions whose state files no longer exist (and grace period expired)
        var toRemove = new List<string>();
        var now = DateTimeOffset.UtcNow;
        foreach (var (sessionId, entry) in _sessions)
        {
            if (!activeIds.Contains(sessionId))
            {
                if (entry.RemoveAfter is null)
                {
                    entry.RemoveAfter = now + GracePeriod;
                }

                if (now >= entry.RemoveAfter)
                {
                    toRemove.Add(sessionId);
                }
            }

            // Also remove sessions that have passed their grace period
            if (entry.RemoveAfter.HasValue && now >= entry.RemoveAfter.Value)
            {
                toRemove.Add(sessionId);
            }
        }

        foreach (var sessionId in toRemove.Distinct())
        {
            RemoveSession(sessionId);
        }

        // Reload workspaces on sweep as well
        ReloadWorkspaces();
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
            entry.Dispose();
            _logger.LogDebug("Session removed: {SessionId}", sessionId);

            // Workspace white dot may reappear now (D11)
            UpdateWorkspaceVisibility();
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
            var agingFactor = entry.LastAgingFactor;
            lines.Add($"{ts} [DUMP] {sid[..Math.Min(8, sid.Length)]} project={d.Project} status={d.Status} hook={d.HookEvent} desktop={entry.DesktopIndex} pack={entry.SoundPack} dismissed={entry.Dismissed} statusAge={age}s seenAge={seenAge}s aging={agingFactor:F2} pid={d.ClaudePid}");

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
                }
                else
                {
                    var wsEntry = new WorkspaceSessionEntry
                    {
                        Workspace = workspace,
                    };
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
                wsEntry.Icon.Visible = result.IsVisible;
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
    }

    // --- Icon Management ---

    private void CreateSessionIcon(SessionEntry entry)
    {
        var (r, g, b) = StatusMap.ResolveColor(entry.State.Status);
        var icon = _agingCache.GetOrCreate(r, g, b, 1.0);

        entry.Icon = new NotifyIcon
        {
            Visible = true,
            Icon = icon,
            Text = FormatSessionTooltip(entry),
        };

        entry.Menu = SessionMenuBuilder.Create(
            entry,
            new SessionMenuCallbacks(
                OnSwitchDesktop: () => SwitchToSessionDesktop(entry),
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
                    _desktopManager.SwitchToDesktop(index);
                },
                OnSetPack: packName =>
                {
                    entry.SoundPack = packName;
                    _logger.LogDebug("Set session {SessionId} sound pack to {Pack}", entry.SessionId, packName ?? "(none)");
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
            getDesktopCount: () => _desktopManager.GetDesktopCount(),
            getDesktopAvailable: () => _desktopManager.IsAvailable,
            logger: _logger);
        entry.Icon.ContextMenuStrip = entry.Menu;

        entry.Icon.Click += (_, e) =>
        {
            entry.LastSeenAt = DateTimeOffset.UtcNow;
            UpdateSessionIcon(entry);

            // Left-click: switch to session's desktop and focus terminal window
            if (e is MouseEventArgs me && me.Button == MouseButtons.Left)
            {
                SwitchToSessionDesktop(entry);
            }
        };

        // Wire balloon click handler at creation time (not deferred to first balloon show).
        // Matches PS1 reference pattern — handler must exist before any ShowBalloonTip call.
        entry.Icon.BalloonTipClicked += (_, _) =>
        {
            _logger.LogInformation("Balloon tip CLICKED for {SessionId}", entry.SessionId);
            entry.LastSeenAt = DateTimeOffset.UtcNow;
            UpdateSessionIcon(entry);

            // Attempt to focus the terminal window.
            // Note: BalloonTipClicked is unreliable on Windows 10+ — the event may not
            // fire, and even when it does, SetForegroundWindow is blocked from notification
            // context. The dot click (Icon.Click) is the reliable focus path.
            // TODO: Migrate to Windows.UI.Notifications toast API for reliable click handling.
            PInvokeWindow.StealForegroundRights();
            SwitchToSessionDesktop(entry);
        };
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
                                return;
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

    private void UpdateSessionIcon(SessionEntry entry)
    {
        if (entry.Icon is null)
        {
            return;
        }

        var (r, g, b) = StatusMap.ResolveColor(entry.State.Status);
        var agingSince = DateTimeOffset.UtcNow - entry.LastSeenAt;
        var factor = CircleIconRenderer.GetAgingFactor(agingSince);
        entry.LastAgingFactor = factor;
        var icon = _agingCache.GetOrCreate(r, g, b, factor);

        entry.Icon.Icon = icon;
        entry.Icon.Text = FormatSessionTooltip(entry);
    }

    private void CreateWorkspaceIcon(WorkspaceSessionEntry entry)
    {
        var (r, g, b) = StatusMap.ResolveColor("workspace");
        var icon = _agingCache.GetOrCreate(r, g, b, 1.0);

        entry.Icon = new NotifyIcon
        {
            Visible = true,
            Icon = icon,
            Text = TooltipFormatter.FormatWorkspace(entry.Workspace.Name, entry.Workspace.Desktop),
        };

        entry.Menu = WorkspaceMenuBuilder.Create(
            entry,
            onUnpin: path =>
            {
                _workspaceStore.Unpin(path);
                ReloadWorkspaces();
            });
        entry.Icon.ContextMenuStrip = entry.Menu;

        entry.Icon.Click += (_, _) =>
        {
            entry.LastSeenAt = DateTimeOffset.UtcNow;
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
            }).ToList(),
            Workspaces = _workspaces.Values.Select(w => new WorkspaceMenuState
            {
                WorkspaceName = w.Workspace.Name,
                WorkspacePath = w.Workspace.Path,
            }).ToList(),
            InstalledPacks = _loadedPacks.Select(p => p.Name).ToList(),
            Config = new ImrdyConfig { Sound = _soundConfig },
            LogPath = ImrdyPaths.MonitorLog,
        };
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
            (_, "idle") when previousStatus == "busy" => SoundEvent.Finished,
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

        // Resolve which pack to use
        var assignment = new PackAssignment(_loadedPacks, _soundConfig);
        var packName = assignment.Resolve(
            entry.SoundPack,
            PathNormalizer.DeriveProject(entry.State?.Cwd ?? ""));

        if (packName is null)
        {
            _logger.LogDebug("No pack resolved for session {SessionId}", entry.SessionId);
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

        // Dispose controller icon
        _controllerIcon.Visible = false;
        _controllerIcon.Icon?.Dispose();
        _controllerIcon.ContextMenuStrip?.Dispose();
        _controllerIcon.Dispose();

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

        _agingCache.Dispose();
        _soundPlayer.Dispose();
        _desktopManager.Dispose();
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

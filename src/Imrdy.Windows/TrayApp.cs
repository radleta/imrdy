using System.Collections.Concurrent;
using Imrdy.Core.Desktop;
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
    private static readonly string TrayDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".imrdy");

    private static readonly string SessionsDir = Path.Combine(TrayDir, "sessions");
    private static readonly string WorkspacesPath = Path.Combine(TrayDir, "workspaces.json");
    private static readonly string SoundsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "sounds");
    private static readonly string SoundsConfigPath = Path.Combine(SoundsDir, "config.json");
    private static readonly string PacksRoot = Path.Combine(SoundsDir, "packs");
    private static readonly string ConfigDir = TrayDir;
    private static readonly string LogPath = Path.Combine(TrayDir, "logs", "monitor.log");
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(60);

    private readonly ILogger _logger;
    private readonly StateFileReader _stateReader;
    private readonly WorkspaceStore _workspaceStore;
    private readonly CooldownTracker _cooldownTracker;
    private readonly PackLoader _packLoader;
    private readonly IDesktopManager _desktopManager;
    private readonly AgingCache _agingCache = new();
    private readonly BalloonTipManager _balloonTipManager;
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
        IDesktopManager desktopManager)
    {
        _logger = loggerFactory.CreateLogger<TrayApp>();
        _stateReader = stateReader;
        _workspaceStore = workspaceStore;
        _cooldownTracker = cooldownTracker;
        _packLoader = packLoader;
        _desktopManager = desktopManager;
        _balloonTipManager = new BalloonTipManager(loggerFactory);

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Application.ApplicationExit += OnApplicationExit;

        _controllerIcon = new NotifyIcon
        {
            Icon = new Icon(typeof(TrayApp), "Resources.imrdy.ico"),
            Text = "imrdy",
            Visible = true,
            ContextMenuStrip = ControllerMenuBuilder.Create(GetControllerState, OnConfigChanged, _logger),
        };

        InitializeDirectories();
        LoadSoundConfig();
        InitializeWatchers();
        InitializeTimers();

        // Initial sweep to pick up existing sessions
        PerformSweep();
        IsBootstrapping = false;

        _logger.LogInformation("TrayApp started — monitoring {Dir}", SessionsDir);
    }

    private void InitializeDirectories()
    {
        if (!Directory.Exists(SessionsDir))
        {
            Directory.CreateDirectory(SessionsDir);
        }
    }

    private void InitializeWatchers()
    {
        // Watch session state files
        _sessionWatcher = new FileSystemWatcher(SessionsDir, "*.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _sessionWatcher.Changed += OnSessionFileChanged;
        _sessionWatcher.Created += OnSessionFileChanged;
        _sessionWatcher.Deleted += OnSessionFileDeleted;

        // Watch workspaces.json
        if (Directory.Exists(TrayDir))
        {
            _workspaceWatcher = new FileSystemWatcher(TrayDir, "workspaces.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _workspaceWatcher.Changed += OnWorkspaceFileChanged;
            _workspaceWatcher.Created += OnWorkspaceFileChanged;
        }

        // Watch sounds config.json for live reload
        if (Directory.Exists(SoundsDir))
        {
            _configWatcher = new FileSystemWatcher(SoundsDir, "config.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _configWatcher.Changed += OnSoundConfigFileChanged;
            _configWatcher.Created += OnSoundConfigFileChanged;
        }
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
    }

    // --- Sound Config ---

    private void LoadSoundConfig()
    {
        try
        {
            _soundConfig = PackAssignment.LoadConfig(SoundsConfigPath);
            _soundEnabled = _soundConfig.SoundEnabled;
            _loadedPacks = _packLoader.LoadPacks(PacksRoot);
            _soundBags.Clear();

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
            _logger.LogDebug("Session updated: {SessionId} → {Status}", state.SessionId, state.Status);
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
            _logger.LogInformation("Session started: {SessionId} ({Project})",
                state.SessionId, state.Project);
        }
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
        if (!Directory.Exists(SessionsDir))
        {
            return;
        }

        var stateFiles = _stateReader.ReadAllStateFiles(SessionsDir);
        var activeIds = new HashSet<string>();

        foreach (var state in stateFiles)
        {
            activeIds.Add(state.SessionId);
            HandleSessionFileChanged(Path.Combine(SessionsDir, $"{state.SessionId}.json"));
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
            if (now - entry.State.Timestamp > StaleThreshold)
            {
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
            entry.Dispose();
            _logger.LogDebug("Session removed: {SessionId}", sessionId);
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reloading workspaces");
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
            onDismiss: () =>
            {
                entry.Dismissed = true;
                RemoveSession(entry.SessionId);
            });
        entry.Icon.ContextMenuStrip = entry.Menu;

        entry.Icon.Click += (_, e) =>
        {
            entry.LastSeenAt = DateTimeOffset.UtcNow;

            // Left-click: switch to session's desktop and focus terminal window
            if (e is MouseEventArgs me && me.Button == MouseButtons.Left)
            {
                SwitchToSessionDesktop(entry);
            }
        };
    }

    private void SwitchToSessionDesktop(SessionEntry entry)
    {
        if (!_desktopManager.IsAvailable)
        {
            return;
        }

        try
        {
            // Try to find the terminal window for this session's Claude PID
            if (entry.State.ClaudePid is int claudePid)
            {
                var hwnd = PInvokeWindow.FindMainWindowForProcess(claudePid);
                if (hwnd != IntPtr.Zero)
                {
                    _desktopManager.FocusWindow(hwnd);
                    return;
                }
            }

            // Fallback: switch to desktop by stored index
            if (entry.DesktopIndex.HasValue)
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
        return new ControllerMenuState(
            Sessions: _sessions.Values.ToList(),
            Workspaces: _workspaces.Values.ToList(),
            InstalledPacks: _loadedPacks.Select(p => p.Name).ToList(),
            Config: _soundConfig,
            ConfigDir: ConfigDir,
            SoundsDir: SoundsDir,
            LogPath: LogPath,
            OnExit: () => ExitThread());
    }

    private void OnConfigChanged(SoundConfig config)
    {
        _soundConfig = config;
        _soundEnabled = config.SoundEnabled;

        // Reload packs if default changed
        _loadedPacks = _packLoader.LoadPacks(PacksRoot);
        _soundBags.Clear();

        _logger.LogDebug("Config updated from controller menu: enabled={Enabled}, default={Default}",
            config.SoundEnabled, config.Default);
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
}

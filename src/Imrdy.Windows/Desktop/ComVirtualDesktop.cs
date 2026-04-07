using System.Runtime.InteropServices;
using Imrdy.Core.Desktop;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Imrdy.Windows.Desktop;

/// <summary>
/// COM interop implementation of IDesktopManager for Windows virtual desktop switching.
/// Uses documented IVirtualDesktopManager (stable CLSID) for window-to-desktop queries
/// and undocumented IVirtualDesktopManagerInternal (build-keyed GUIDs) for desktop switching.
/// Gracefully degrades on unknown builds or COM failure.
/// Recovers from Explorer restarts via lazy re-init on COMException.
/// </summary>
internal sealed class ComVirtualDesktop : IDesktopManager
{
    private readonly ILogger _logger;
    private readonly int _buildNumber;
    private readonly Guid? _internalIid;
    private readonly Guid? _virtualDesktopIid;
    private readonly object _lock = new();

    private bool _disposed;
    private bool _available;

    // Documented COM interface — stable across builds
    private IVirtualDesktopManager? _desktopManager;

    // Undocumented COM interface — accessed via raw vtable calls
    private IntPtr _internalPtr;

    public bool IsAvailable => _available && !_disposed;

    private bool IsWindows11 => _buildNumber >= 22000;

    public ComVirtualDesktop(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ComVirtualDesktop>();
        _buildNumber = Environment.OSVersion.Version.Build;
        _internalIid = VirtualDesktopGuids.GetInternalIid(_buildNumber);
        _virtualDesktopIid = VirtualDesktopGuids.GetVirtualDesktopIid(_buildNumber);

        if (_internalIid is null || _virtualDesktopIid is null)
        {
            _logger.LogWarning("Unknown Windows build {Build} — virtual desktop switching disabled",
                _buildNumber);
            _available = false;
            return;
        }

        Initialize();
    }

    public void Reinitialize()
    {
        lock (_lock)
        {
            ReleaseCom();
            Initialize();
        }
    }

    public int? GetCurrentDesktopIndex()
    {
        if (!IsAvailable)
        {
            return null;
        }

        try
        {
            return WithComRecovery(() =>
            {
                var currentId = GetCurrentDesktopId();
                if (currentId == Guid.Empty)
                {
                    return (int?)null;
                }

                var desktops = GetDesktopIds();
                if (desktops.Count == 0)
                {
                    desktops = GetDesktopIdsFromRegistry();
                }

                for (var i = 0; i < desktops.Count; i++)
                {
                    if (desktops[i] == currentId)
                    {
                        return i;
                    }
                }

                return null;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get current desktop index");
            return null;
        }
    }

    public int? GetDesktopCount()
    {
        if (!IsAvailable)
        {
            return null;
        }

        try
        {
            return WithComRecovery(() =>
            {
                var desktops = GetDesktopIds();
                if (desktops.Count == 0)
                {
                    desktops = GetDesktopIdsFromRegistry();
                }

                return desktops.Count > 0 ? desktops.Count : (int?)null;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get desktop count");
            return null;
        }
    }

    public int? GetDesktopForWindow(IntPtr hwnd)
    {
        if (!IsAvailable || hwnd == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return WithComRecovery(() =>
            {
                if (_desktopManager is null)
                {
                    return (int?)null;
                }

                var hr = _desktopManager.GetWindowDesktopId(hwnd, out var desktopId);
                if (hr < 0 || desktopId == Guid.Empty)
                {
                    return null;
                }

                var desktops = GetDesktopIds();
                if (desktops.Count == 0)
                {
                    desktops = GetDesktopIdsFromRegistry();
                }

                for (var i = 0; i < desktops.Count; i++)
                {
                    if (desktops[i] == desktopId)
                    {
                        return i;
                    }
                }

                return null;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get desktop for window {Handle}", hwnd);
            return null;
        }
    }

    public void SwitchToDesktop(int index)
    {
        if (!IsAvailable || index < 0)
        {
            return;
        }

        try
        {
            WithComRecovery(() =>
            {
                var desktops = GetDesktopIds();
                if (desktops.Count == 0)
                {
                    desktops = GetDesktopIdsFromRegistry();
                }

                if (desktops.Count == 0)
                {
                    _logger.LogDebug("No desktops found for SwitchToDesktop");
                    return;
                }

                if (index >= desktops.Count)
                {
                    _logger.LogDebug("Desktop index {Index} out of range (count: {Count})",
                        index, desktops.Count);
                    return;
                }

                SwitchDesktopById(desktops[index]);
                _logger.LogDebug("Switched to desktop {Index} via COM", index);
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to switch to desktop {Index}", index);
        }
    }

    public void FocusWindow(IntPtr hwnd)
    {
        if (!IsAvailable || hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // First switch to the window's desktop
            var desktopIndex = GetDesktopForWindow(hwnd);
            if (desktopIndex.HasValue)
            {
                var currentIndex = GetCurrentDesktopIndex();
                if (currentIndex != desktopIndex)
                {
                    SwitchToDesktop(desktopIndex.Value);
                }
            }

            // Then bring the window to foreground
            PInvokeWindow.ForceForeground(hwnd);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to focus window {Handle}", hwnd);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseCom();
    }

    // --- COM Initialization ---

    private void Initialize()
    {
        try
        {
            // Create documented IVirtualDesktopManager
            var comType = Type.GetTypeFromCLSID(VirtualDesktopGuids.CLSID_VirtualDesktopManager);
            if (comType is null)
            {
                _logger.LogWarning("Failed to get COM type for VirtualDesktopManager");
                _available = false;
                return;
            }

            var obj = Activator.CreateInstance(comType);
            if (obj is null)
            {
                _logger.LogWarning("Failed to create VirtualDesktopManager COM instance");
                _available = false;
                return;
            }

            _desktopManager = (IVirtualDesktopManager)obj;

            // Get undocumented IVirtualDesktopManagerInternal via ImmersiveShell service
            _internalPtr = GetManagerInternal();
            if (_internalPtr == IntPtr.Zero)
            {
                _logger.LogWarning("Failed to get IVirtualDesktopManagerInternal — " +
                    "desktop switching disabled, window queries still work");
                // Still partially available — can query window desktops via documented API
                _available = true;
                return;
            }

            _available = true;
            _logger.LogInformation("Virtual desktop COM initialized for build {Build}", _buildNumber);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize virtual desktop COM");
            _available = false;
        }
    }

    private IntPtr GetManagerInternal()
    {
        if (_internalIid is null)
        {
            return IntPtr.Zero;
        }

        try
        {
            // Get ImmersiveShell (the service provider)
            var shellType = Type.GetTypeFromCLSID(VirtualDesktopGuids.CLSID_ImmersiveShell);
            if (shellType is null)
            {
                return IntPtr.Zero;
            }

            var shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return IntPtr.Zero;
            }

            try
            {
                // QueryService for IVirtualDesktopManagerInternal
                var serviceProvider = (IServiceProvider10)shell;
                var sid = VirtualDesktopGuids.SID_VirtualDesktopManagerInternal;
                var iid = _internalIid.Value;
                var hr = serviceProvider.QueryService(ref sid, ref iid, out var ppvObject);
                if (hr < 0 || ppvObject == IntPtr.Zero)
                {
                    _logger.LogWarning("QueryService failed for IVirtualDesktopManagerInternal " +
                        "(HRESULT: 0x{Hr:X8})", hr);
                    return IntPtr.Zero;
                }

                return ppvObject;
            }
            finally
            {
                Marshal.ReleaseComObject(shell);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get IVirtualDesktopManagerInternal");
            return IntPtr.Zero;
        }
    }

    private void ReleaseCom()
    {
        if (_desktopManager is not null)
        {
            try
            {
                Marshal.ReleaseComObject(_desktopManager);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error releasing IVirtualDesktopManager");
            }

            _desktopManager = null;
        }

        if (_internalPtr != IntPtr.Zero)
        {
            try
            {
                Marshal.Release(_internalPtr);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error releasing IVirtualDesktopManagerInternal");
            }

            _internalPtr = IntPtr.Zero;
        }

        _available = false;
    }

    // --- COM Recovery ---

    private T WithComRecovery<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (COMException ex) when (IsCriticalComFailure(ex))
        {
            _logger.LogWarning(ex, "COM failure detected (Explorer restart?), attempting recovery");
            Reinitialize();
            return action(); // Retry once after re-init
        }
        catch (InvalidComObjectException ex)
        {
            _logger.LogWarning(ex, "Invalid COM object detected, attempting recovery");
            Reinitialize();
            return action();
        }
        catch (AccessViolationException ex)
        {
            _logger.LogError(ex, "COM access violation — disabling virtual desktop support");
            _available = false;
            return default!;
        }
        catch (SEHException ex)
        {
            _logger.LogError(ex, "COM SEH exception — disabling virtual desktop support");
            _available = false;
            return default!;
        }
    }

    private void WithComRecovery(Action action)
    {
        WithComRecovery(() =>
        {
            action();
            return 0;
        });
    }

    private static bool IsCriticalComFailure(COMException ex)
    {
        // RPC_E_DISCONNECTED, CO_E_OBJNOTCONNECTED, RPC_S_SERVER_UNAVAILABLE
        return ex.HResult is unchecked((int)0x80010108)
            or unchecked((int)0x800401FD)
            or unchecked((int)0x800706BA);
    }

    // --- Vtable helper ---

    /// <summary>
    /// Reads a function pointer from a COM vtable at the given slot index
    /// and returns it as a typed delegate. Slots are zero-based from IUnknown
    /// (0=QueryInterface, 1=AddRef, 2=Release, 3+ = interface methods).
    /// Sources: MScholtes/VirtualDesktop, Grabacr07/VirtualDesktop.
    /// </summary>
    private static T GetVtableDelegate<T>(IntPtr comPtr, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(comPtr);
        var funcPtr = Marshal.ReadIntPtr(vtable + IntPtr.Size * slot);
        return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);
    }

    // --- Undocumented COM vtable calls ---

    private Guid GetCurrentDesktopId()
    {
        if (_internalPtr == IntPtr.Zero)
        {
            return Guid.Empty;
        }

        // IVirtualDesktopManagerInternal::GetCurrentDesktop — vtable slot 6
        int hr;
        IntPtr desktopPtr;
        if (IsWindows11)
        {
            var fn = GetVtableDelegate<GetCurrentDesktopDelegate_Win11>(_internalPtr, 6);
            hr = fn(_internalPtr, IntPtr.Zero, out desktopPtr);
        }
        else
        {
            var fn = GetVtableDelegate<GetCurrentDesktopDelegate_Win10>(_internalPtr, 6);
            hr = fn(_internalPtr, out desktopPtr);
        }
        if (hr < 0 || desktopPtr == IntPtr.Zero)
        {
            return Guid.Empty;
        }

        try
        {
            return GetDesktopId(desktopPtr);
        }
        finally
        {
            Marshal.Release(desktopPtr);
        }
    }

    private IReadOnlyList<Guid> GetDesktopIds()
    {
        if (_internalPtr == IntPtr.Zero)
        {
            return Array.Empty<Guid>();
        }

        // IVirtualDesktopManagerInternal::GetDesktops — vtable slot 7
        int hr;
        IntPtr arrayPtr;
        if (IsWindows11)
        {
            var fn = GetVtableDelegate<GetDesktopsDelegate_Win11>(_internalPtr, 7);
            hr = fn(_internalPtr, IntPtr.Zero, out arrayPtr);
        }
        else
        {
            var fn = GetVtableDelegate<GetDesktopsDelegate_Win10>(_internalPtr, 7);
            hr = fn(_internalPtr, out arrayPtr);
        }
        if (hr < 0 || arrayPtr == IntPtr.Zero)
        {
            _logger.LogDebug("GetDesktopIds failed: hr=0x{Hr:X8}", hr);
            return Array.Empty<Guid>();
        }

        try
        {
            return EnumerateObjectArray(arrayPtr);
        }
        finally
        {
            Marshal.Release(arrayPtr);
        }
    }

    /// <summary>
    /// Reads virtual desktop GUIDs from the registry. Fallback for when the
    /// undocumented COM GetDesktops vtable slot fails (e.g., RPC_S_CANNOT_SUPPORT on build 19045).
    /// The VirtualDesktopIDs value is a binary blob of 16-byte GUIDs.
    /// </summary>
    private IReadOnlyList<Guid> GetDesktopIdsFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops");
            if (key?.GetValue("VirtualDesktopIDs") is not byte[] blob || blob.Length < 16)
            {
                return Array.Empty<Guid>();
            }

            var count = blob.Length / 16;
            var guids = new List<Guid>(count);
            for (var i = 0; i < count; i++)
            {
                var guidBytes = new byte[16];
                Buffer.BlockCopy(blob, i * 16, guidBytes, 0, 16);
                guids.Add(new Guid(guidBytes));
            }

            _logger.LogDebug("GetDesktopIds from registry: {Count} desktops", count);
            return guids;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read desktop IDs from registry");
            return Array.Empty<Guid>();
        }
    }

    private void SwitchDesktopById(Guid desktopId)
    {
        if (_internalPtr == IntPtr.Zero)
        {
            return;
        }

        // IVirtualDesktopManagerInternal::FindDesktop
        // Win10: slot 12 (F31574D6), Win11: slot 13 (B2F925B9)
        var findSlot = IsWindows11 ? 13 : 12;
        var findFn = GetVtableDelegate<FindDesktopDelegate>(_internalPtr, findSlot);
        var hr = findFn(_internalPtr, ref desktopId, out var desktopPtr);
        if (hr < 0 || desktopPtr == IntPtr.Zero)
        {
            _logger.LogDebug("FindDesktop failed for {DesktopId} (HRESULT: 0x{Hr:X8})",
                desktopId, hr);
            return;
        }

        try
        {
            // IVirtualDesktopManagerInternal::SwitchDesktop — vtable slot 9
            if (IsWindows11)
            {
                var switchFn = GetVtableDelegate<SwitchDesktopDelegate_Win11>(_internalPtr, 9);
                hr = switchFn(_internalPtr, IntPtr.Zero, desktopPtr);
            }
            else
            {
                var switchFn = GetVtableDelegate<SwitchDesktopDelegate_Win10>(_internalPtr, 9);
                hr = switchFn(_internalPtr, desktopPtr);
            }

            if (hr < 0)
            {
                _logger.LogDebug("SwitchDesktop failed (HRESULT: 0x{Hr:X8})", hr);
            }
        }
        finally
        {
            Marshal.Release(desktopPtr);
        }
    }

    private static Guid GetDesktopId(IntPtr desktopPtr)
    {
        // IVirtualDesktop::GetID — vtable slot 4
        // Slot 3 is IsViewVisible on both Win10 and Win11; GetID is always slot 4.
        var fn = GetVtableDelegate<GetIdDelegate>(desktopPtr, 4);
        var hr = fn(desktopPtr, out var id);
        return hr < 0 ? Guid.Empty : id;
    }

    private IReadOnlyList<Guid> EnumerateObjectArray(IntPtr arrayPtr)
    {
        // IObjectArray::GetCount — vtable slot 3
        var getCountFn = GetVtableDelegate<GetCountDelegate>(arrayPtr, 3);
        var hr = getCountFn(arrayPtr, out var count);
        if (hr < 0 || count == 0)
        {
            return Array.Empty<Guid>();
        }

        // IObjectArray::GetAt — vtable slot 4
        var getAtFn = GetVtableDelegate<GetAtDelegate>(arrayPtr, 4);
        var desktopIid = _virtualDesktopIid!.Value;
        var result = new List<Guid>(Math.Min((int)count, 256));

        for (uint i = 0; i < count; i++)
        {
            hr = getAtFn(arrayPtr, i, ref desktopIid, out var itemPtr);
            if (hr < 0 || itemPtr == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                var id = GetDesktopId(itemPtr);
                if (id != Guid.Empty)
                {
                    result.Add(id);
                }
            }
            finally
            {
                Marshal.Release(itemPtr);
            }
        }

        return result;
    }

    // --- COM Delegates (vtable function signatures) ---
    // Windows 10 (builds <22000): no hWndOrMonitor parameter
    // Windows 11 (builds >=22000): added hWndOrMonitor parameter to GetCurrentDesktop, GetDesktops, SwitchDesktop

    // Windows 10 signatures
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCurrentDesktopDelegate_Win10(IntPtr @this, out IntPtr desktop);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesktopsDelegate_Win10(IntPtr @this, out IntPtr objectArray);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SwitchDesktopDelegate_Win10(IntPtr @this, IntPtr desktop);

    // Windows 11 signatures
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCurrentDesktopDelegate_Win11(IntPtr @this, IntPtr hWndOrMonitor,
        out IntPtr desktop);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesktopsDelegate_Win11(IntPtr @this, IntPtr hWndOrMonitor,
        out IntPtr objectArray);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SwitchDesktopDelegate_Win11(IntPtr @this, IntPtr hWndOrMonitor,
        IntPtr desktop);

    // Shared across builds
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FindDesktopDelegate(IntPtr @this, ref Guid desktopId,
        out IntPtr desktop);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetIdDelegate(IntPtr @this, out Guid id);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCountDelegate(IntPtr @this, out uint count);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetAtDelegate(IntPtr @this, uint index, ref Guid riid,
        out IntPtr ppvObject);

    // --- Documented COM Interfaces ---

    /// <summary>
    /// Documented IVirtualDesktopManager COM interface.
    /// Stable across all Windows 10/11 builds.
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow,
            [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }

    /// <summary>
    /// Undocumented IServiceProvider for Shell services.
    /// Used to get IVirtualDesktopManagerInternal via QueryService.
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
    private interface IServiceProvider10
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }
}

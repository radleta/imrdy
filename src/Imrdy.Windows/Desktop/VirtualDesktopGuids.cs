namespace Imrdy.Windows.Desktop;

/// <summary>
/// Static GUID table for undocumented IVirtualDesktopManagerInternal COM interface.
/// Keyed by Windows build number. Easy to update: just add a row for new builds.
/// Sources: MScholtes/VirtualDesktop, Grabacr07/VirtualDesktop.
/// </summary>
internal static class VirtualDesktopGuids
{
    /// <summary>
    /// CLSID for the documented VirtualDesktopManager COM class (stable across builds).
    /// </summary>
    public static readonly Guid CLSID_VirtualDesktopManager =
        new("aa509086-5ca9-4c25-8f95-589d3c07b48a");

    /// <summary>
    /// IID for the documented IVirtualDesktopManager interface (stable across builds).
    /// </summary>
    public static readonly Guid IID_IVirtualDesktopManager =
        new("a5cd92ff-29be-454c-8d04-d82879fb3f1b");

    /// <summary>
    /// CLSID for the undocumented VirtualDesktopManagerInternal service (stable across builds).
    /// Accessed via IServiceProvider10::QueryService.
    /// </summary>
    public static readonly Guid CLSID_ImmersiveShell =
        new("c2f03a33-21f5-47fa-b4bb-156362a2f239");

    /// <summary>
    /// SID for the virtual desktop manager internal service (stable across builds).
    /// </summary>
    public static readonly Guid SID_VirtualDesktopManagerInternal =
        new("c5e0cdca-7b6e-41b2-9fc4-d93975cc467b");

    /// <summary>
    /// IVirtualDesktopManagerInternal layouts to try for a build, newest first; empty for unknown builds.
    /// The build number alone cannot pick one: servicing updates change the IID within a build
    /// (26200.9445 rejects a3175f2d and accepts 53f5ca0b), so the caller keeps the first IID
    /// QueryService accepts and dispatches with that entry's slots.
    /// </summary>
    public static IReadOnlyList<InternalLayout> GetInternalLayouts(int buildNumber)
    {
        return buildNumber switch
        {
            // Windows 10 20H1 through 22H2 (builds 19041-19045)
            >= 19041 and <= 19045 => [new(new Guid("f31574d6-b682-4cdc-bd56-1827860abec6"), HasMonitorArg: false, FindDesktopSlot: 12)],

            >= 22000 =>
            [
                // Current Windows 11 servicing; layout per MScholtes/VirtualDesktop VirtualDesktop11-24H2.cs
                new(new Guid("53f5ca0b-158f-4124-900c-057158060b27"), HasMonitorArg: false, FindDesktopSlot: 14),
                new(new Guid("a3175f2d-239c-4b68-8e68-2af1a8a3b0bf"), HasMonitorArg: true, FindDesktopSlot: 13),
                new(new Guid("b2f925b9-5a0f-4d2e-9f4d-2b1507593c10"), HasMonitorArg: true, FindDesktopSlot: 13),
            ],

            _ => [],
        };
    }

    /// <summary>
    /// Maps Windows build numbers to IVirtualDesktop IIDs.
    /// Returns null for unknown builds.
    /// </summary>
    public static Guid? GetVirtualDesktopIid(int buildNumber)
    {
        return buildNumber switch
        {
            // Windows 10 20H1-22H2
            >= 19041 and <= 19045 => new Guid("ff72ffdd-be7e-43fc-9c03-ad81681e88e4"),

            // Windows 11 21H2 (different IVirtualDesktop IID)
            22000 => new Guid("536d3495-b208-4cc9-ae26-de8111275bf8"),

            // Windows 11 22H2+ (builds 22621+)
            >= 22621 => new Guid("3f07f4be-b107-441a-af0f-39d82529072c"),

            _ => null,
        };
    }
}

/// <param name="HasMonitorArg">GetCurrentDesktop, GetDesktops and SwitchDesktop take a leading hWndOrMonitor.</param>
internal readonly record struct InternalLayout(Guid Iid, bool HasMonitorArg, int FindDesktopSlot);

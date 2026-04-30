namespace Imrdy.Core.Wsl;

/// <summary>
/// Represents a WSL distro found during auto-discovery (Step 08).
/// Defined here so WslDistroStore.Reconcile can compile before WslDistroDiscovery exists.
/// </summary>
public sealed record DiscoveredDistro(string Name, IReadOnlyList<string> LinuxHomes);

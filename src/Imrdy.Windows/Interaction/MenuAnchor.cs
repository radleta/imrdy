using System.Drawing;
using System.Windows.Forms;

namespace Imrdy.Windows.Interaction;

/// <summary>
/// Value type describing where to anchor a <see cref="ContextMenuStrip"/> when the
/// interaction router opens a menu. The two anchoring modes map directly to the two
/// surfaces that can source a right-click:
/// <list type="bullet">
///   <item><description><see cref="AtTrayIcon"/> — the shell-delivered <see cref="NotifyIcon"/> click;
///     dispatched via <c>NotifyIconMenuHost</c> (reflection-based private API).</description></item>
///   <item><description><see cref="AtControl"/> — a WinForms <see cref="Control"/> (the overlay form)
///     with a real client-coordinate point; dispatched via <c>menu.Show(owner, location)</c>.</description></item>
/// </list>
/// Keep this as the single abstraction — callers should never reach for <c>NotifyIconMenuHost</c>
/// or <c>menu.Show</c> directly, only through the router.
/// </summary>
internal readonly record struct MenuAnchor
{
    public NotifyIcon? TrayIcon { get; }
    public Control? Owner { get; }
    public Point Location { get; }

    private MenuAnchor(NotifyIcon? trayIcon, Control? owner, Point location)
    {
        TrayIcon = trayIcon;
        Owner = owner;
        Location = location;
    }

    public static MenuAnchor AtTrayIcon(NotifyIcon icon) => new(icon, null, default);

    public static MenuAnchor AtControl(Control owner, Point clientLocation) => new(null, owner, clientLocation);
}

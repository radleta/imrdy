using System.Drawing;
using System.Windows.Forms;
using FluentAssertions;
using Imrdy.Core;
using Imrdy.Core.Menus;
using Imrdy.Core.State;
using Imrdy.Windows.Menus;
using Imrdy.Windows.Models;
using Xunit;

namespace Imrdy.Windows.Tests.Menus;

/// <summary>
/// Live-<see cref="ContextMenuStrip"/> regression coverage for the Step 08
/// first-right-click-eaten fix. <see cref="Imrdy.Windows.Tests.Menus.MenuOpeningPolicyTests"/>
/// covers the pure decision logic; this class proves the fix actually works against the real
/// WinForms control that produced the bug, using the same <c>menu.Show(owner, location)</c>
/// call TrayApp.ShowContextMenuAt uses for the AtControl anchor path.
/// </summary>
/// <remarks>
/// <see cref="ContextMenuStrip.OnOpening"/> pre-sets <c>e.Cancel</c> and raises
/// <c>Opening</c> synchronously inside <see cref="ToolStrip.Show(Control, Point)"/> — but
/// <c>MenuRenderer.Apply</c> (called from every builder's Opening handler) asserts
/// <see cref="Application.MessageLoop"/>, i.e. a genuinely running <see cref="Application.Run"/>
/// pump on the calling thread — a bare STA thread with no pump active (the shape
/// <c>InspectServiceTests</c> uses for its offscreen <see cref="Form"/> walk, which needs no
/// such pump) fails that assert and the Opening handler's catch swallows it, silently
/// reproducing a *different* zero-items failure than the one under test. <see cref="RunOnSta"/>
/// therefore runs a real message loop via <see cref="Application.Run(ApplicationContext)"/> and
/// dispatches <paramref name="testBody"/> through it via a one-shot <see cref="System.Windows.Forms.Timer"/>
/// tick, exactly mirroring how production reaches <c>ShowContextMenuAt</c> — synchronously
/// inside a message already being pumped by the running <c>TrayApp</c>.
/// </remarks>
public class MenuOpeningEndToEndTests
{
    private static void RunOnSta(Action testBody)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var appContext = new ApplicationContext();
                using var pumpTrigger = new System.Windows.Forms.Timer { Interval = 1 };
                pumpTrigger.Tick += (_, _) =>
                {
                    pumpTrigger.Stop();
                    try { testBody(); }
                    catch (Exception ex) { threadEx = ex; }
                    finally { appContext.ExitThread(); }
                };
                pumpTrigger.Start();
                Application.Run(appContext);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (threadEx is not null)
        {
            throw new Exception("Exception on STA test thread", threadEx);
        }
    }

    private static Form CreateOffscreenOwner()
    {
        var owner = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            ShowInTaskbar = false,
        };
        owner.Show();
        return owner;
    }

    [Fact]
    public void SessionMenuBuilder_FirstShowOnFreshInstance_MenuBecomesVisible()
    {
        RunOnSta(() =>
        {
            var owner = CreateOffscreenOwner();
            try
            {
                var entry = new SessionEntry
                {
                    SessionId = "test-session",
                    State = new StateFileModel
                    {
                        SessionId = "test-session",
                        Status = "idle",
                        Project = "test-project",
                        Cwd = "/tmp",
                        HookEvent = "Stop",
                    },
                };
                var callbacks = new SessionMenuCallbacks(
                    OnSwitchDesktop: () => { },
                    OnAssignDesktop: () => { },
                    OnSetDesktop: _ => { },
                    OnSetPack: _ => { },
                    OnSetIconStyle: _ => { },
                    OnPinWorkspace: () => { },
                    OnUnpinWorkspace: () => { },
                    OnClear: () => { },
                    OnClearAll: () => { },
                    OnDumpState: () => { },
                    OnExit: () => { });

                var menu = SessionMenuBuilder.Create(
                    entry,
                    callbacks,
                    getInstalledPacks: () => [],
                    getInstalledGraphicsPacks: () => [],
                    getDesktopCount: () => 4,
                    getDesktopAvailable: () => true,
                    getIsPinned: () => false);

                try
                {
                    // Regression assertion: before the Step 08 fix, this FIRST Show() on a
                    // freshly-constructed (zero-item) menu left menu.Visible == false, because
                    // ContextMenuStrip.OnOpening pre-set e.Cancel = true (Items.Count == 0 at
                    // that moment) and the Opening handler rebuilt 15 items without ever
                    // clearing it.
                    menu.Show(owner, new Point(10, 10));

                    menu.Items.Count.Should().BeGreaterThan(0, "the Opening rebuild should have populated items");
                    menu.Visible.Should().BeTrue(
                        "WinForms must not refuse to display a menu whose Opening handler populated items");
                }
                finally
                {
                    menu.Close();
                    menu.Dispose();
                }
            }
            finally
            {
                owner.Hide();
                owner.Dispose();
            }
        });
    }

    [Fact]
    public void OverlayMenuBuilder_FirstShowOnFreshInstance_MenuBecomesVisible()
    {
        RunOnSta(() =>
        {
            var owner = CreateOffscreenOwner();
            try
            {
                var state = new ControllerMenuState
                {
                    Sessions = [],
                    Workspaces = [],
                    InstalledPacks = [],
                    InstalledGraphicsPacks = [],
                    Monitors = [],
                    Config = new ImrdyConfig(),
                    LogPath = "test.log",
                    OverlayWorkingArea = new Rectangle(0, 0, 1920, 1080),
                    OverlayPanelSize = new Size(64, 64),
                };

                var menu = OverlayMenuBuilder.Create(
                    stateProvider: () => state,
                    onConfigChanged: _ => { },
                    logger: null);

                try
                {
                    // Same regression as the session-menu test above, exercised via the
                    // stateProvider-based Create() shape (also used by ControllerMenuBuilder)
                    // rather than the entry-based shape (also used by WorkspaceMenuBuilder) —
                    // this is the overlay gutter menu that originally reported the defect.
                    menu.Show(owner, new Point(10, 10));

                    menu.Items.Count.Should().BeGreaterThan(0, "the overlay submenu always has fixed items (positions/spacing/monitor/lock)");
                    menu.Visible.Should().BeTrue(
                        "WinForms must not refuse to display a menu whose Opening handler populated items");
                }
                finally
                {
                    menu.Close();
                    menu.Dispose();
                }
            }
            finally
            {
                owner.Hide();
                owner.Dispose();
            }
        });
    }
}

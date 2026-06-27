using System.Drawing;
using System.Windows.Forms;
using Imrdy.Core.Desktop;
using Imrdy.Core.Display;
using Imrdy.Windows.Theme;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Dashboard;

/// <summary>
/// Non-layered WinForms hover dashboard for a workspace. Surfaces workspace identity:
/// Name, Path, Desktop, IconStyle, ActivityText, Git info. No recent-tools strip and no
/// sparkline — workspaces are not sessions.
///
/// Derives from <see cref="HoverDashboardFormBase"/> which owns: form shell init,
/// DWM mica/acrylic backdrop, rounded Region clip, WM_MOUSEACTIVATE focus guard,
/// Pin/Unpin API, Escape key handling, and anchor-edge placement.
/// </summary>
internal sealed class WorkspaceDashboardForm : HoverDashboardFormBase
{
    private WorkspaceDashboardViewModel _vm;
    private readonly ILogger _logger;

    // ---- Layout child controls (field-promoted for Update() access) ----

    // Header
    private readonly Label _nameLabel;
    private readonly Label _desktopChip;

    // Subtitle row
    private readonly Label _pathLabel;
    private readonly Label _iconStyleChip;

    // Activity row
    private readonly Label _activityLabel;

    // Git row (chips cleared+rebuilt on each Update)
    private readonly FlowLayoutPanel _gitRow;

    // TableLayoutPanel row indices
    private TableLayoutPanel _tableLayout = null!; // assigned in BuildLayout, called from ctor
    private const int RowHeader   = 0;
    private const int RowActivity = 1;
    private const int RowGit      = 2;
    private const int RowFooter   = 3;

    private const int GitRowHeight = 36;

    public WorkspaceDashboardForm(
        WorkspaceDashboardViewModel vm,
        IDesktopManager? desktopManager,
        ILoggerFactory loggerFactory)
        : base(desktopManager, loggerFactory)
    {
        _vm     = vm;
        _logger = loggerFactory.CreateLogger<WorkspaceDashboardForm>();

        BackColor = ImrdyPalette.BgForm;

        // Build field controls (VM-agnostic construction)
        _nameLabel = new Label
        {
            Font         = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor    = ImrdyPalette.FgPrimary,
            BackColor    = Color.Transparent,
            AutoSize     = false,
            AutoEllipsis = true,
            Width        = 300,
            Height       = 26,
            Padding      = Padding.Empty,
            Margin       = Padding.Empty,
        };

        _desktopChip = new Label
        {
            Font      = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ImrdyPalette.FgSecondary,
            BackColor = Color.FromArgb(15, 255, 255, 255),
            AutoSize  = true,
            Padding   = new Padding(5, 1, 5, 1),
            Margin    = Padding.Empty,
        };
        _desktopChip.Paint += (_, pe) =>
        {
            var r = new Rectangle(0, 0, _desktopChip.Width - 1, _desktopChip.Height - 1);
            using var pen = new Pen(Color.FromArgb(80, 255, 255, 255), 1f);
            pe.Graphics.DrawRectangle(pen, r);
        };

        _pathLabel = new Label
        {
            Font         = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor    = ImrdyPalette.FgMuted,
            BackColor    = Color.Transparent,
            AutoSize     = false,
            AutoEllipsis = true,
            Height       = 18,
            Padding      = Padding.Empty,
            Margin       = new Padding(0, 0, 8, 0),
        };

        _iconStyleChip = new Label
        {
            Font      = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ImrdyPalette.FgSecondary,
            BackColor = Color.FromArgb(15, 255, 255, 255),
            AutoSize  = true,
            Padding   = new Padding(5, 1, 5, 1),
            Margin    = Padding.Empty,
        };
        _iconStyleChip.Paint += (_, pe) =>
        {
            var r = new Rectangle(0, 0, _iconStyleChip.Width - 1, _iconStyleChip.Height - 1);
            using var pen = new Pen(Color.FromArgb(80, 255, 255, 255), 1f);
            pe.Graphics.DrawRectangle(pen, r);
        };

        _activityLabel = new Label
        {
            Font      = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ImrdyPalette.FgSecondary,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Padding   = Padding.Empty,
            Margin    = Padding.Empty,
        };

        _gitRow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            BackColor     = ImrdyPalette.BgForm,
            Padding       = new Padding(14, 4, 14, 0),
            Margin        = new Padding(8, 4, 8, 4),
        };

        BuildLayout();
        Update(vm);

        _logger.LogDebug(
            "WorkspaceDashboardForm: created workspace={Name} path={Path}",
            vm.Name, vm.WorkspacePath);
    }

    // ---- Layout construction ----

    private void BuildLayout()
    {
        SuspendLayout();

        var innerWidth = FormMinWidth - 12 - 8;

        // Header panel
        var headerPanel = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = ImrdyPalette.BgForm,
            Margin    = new Padding(8, 4, 8, 4),
        };

        // 3px vertical accent bar on left edge (same as SessionDashboardForm)
        var accentBar = new Panel
        {
            Width     = 3,
            Dock      = DockStyle.Left,
            BackColor = Color.FromArgb(100, 180, 220),
        };
        headerPanel.Controls.Add(accentBar);

        // Name row: name label + desktop chip
        var nameRow = new FlowLayoutPanel
        {
            Left          = 12,
            Top           = 16,
            Width         = innerWidth,
            Height        = 28,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            BackColor     = Color.Transparent,
            Anchor        = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
        };
        nameRow.Controls.Add(_nameLabel);
        nameRow.Controls.Add(_desktopChip);

        // Subtitle row: path label + iconStyle chip
        var subtitleRow = new FlowLayoutPanel
        {
            Left          = 12,
            Top           = 44, // 16 (nameRow.Top) + 28 (nameRow.Height)
            Width         = innerWidth,
            Height        = 20,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            BackColor     = Color.Transparent,
            Anchor        = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
        };
        subtitleRow.Controls.Add(_pathLabel);
        subtitleRow.Controls.Add(_iconStyleChip);

        headerPanel.Controls.Add(nameRow);
        headerPanel.Controls.Add(subtitleRow);

        // Activity row
        var activityRow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            BackColor     = ImrdyPalette.BgForm,
            Padding       = new Padding(14, 4, 14, 0),
            Margin        = new Padding(8, 4, 8, 4),
        };
        activityRow.Controls.Add(_activityLabel);

        // Footer panel
        var footer = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = ImrdyPalette.BgFooter,
            Margin    = new Padding(0),
        };
        var footerTlp = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 1,
            BackColor   = ImrdyPalette.BgFooter,
            Padding     = new Padding(22, 9, 22, 9),
            Margin      = Padding.Empty,
        };
        footerTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        footerTlp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footerTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var leftPlaceholder = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = Color.Transparent,
        };
        var kbdHints = MakeKbdHintsFlow();
        kbdHints.Margin = Padding.Empty;
        footerTlp.Controls.Add(leftPlaceholder, 0, 0);
        footerTlp.Controls.Add(kbdHints,         1, 0);
        footer.Controls.Add(footerTlp);

        // TableLayoutPanel: 4 rows
        _tableLayout = new TableLayoutPanel
        {
            AutoSize     = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount  = 1,
            RowCount     = 4,
            BackColor    = ImrdyPalette.BgForm,
            Padding      = Padding.Empty,
            Margin       = Padding.Empty,
            Left         = 0,
            Top          = 0,
            Width        = FormMinWidth,
            Anchor       = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
        };
        _tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // Row 0: Header — Absolute 78px (always visible;
        //   top cushion clears 14px corner Region, same budget as SessionDashboardForm)
        _tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
        // Row 1: Activity row — Absolute 36px (always visible)
        _tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        // Row 2: Git block — 0 when null; GitRowHeight(36) when present
        _tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
        // Row 3: Footer — Absolute 38px (always visible)
        _tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));

        _tableLayout.Controls.Add(headerPanel,  0, RowHeader);
        _tableLayout.Controls.Add(activityRow,  0, RowActivity);
        _tableLayout.Controls.Add(_gitRow,       0, RowGit);
        _tableLayout.Controls.Add(footer,        0, RowFooter);

        Controls.Add(_tableLayout);

        ResumeLayout(false);
    }

    // ---- Public API ----

    /// <summary>
    /// Shows the form using the anchor already set by PlaceWithAnchor.
    /// </summary>
    public void Show(WorkspaceDashboardViewModel vm)
    {
        Update(vm);
        if (!Visible)
            base.Show();
    }

    /// <summary>
    /// Applies an updated view model to the form. Refreshes all dynamic fields so that
    /// workspace→workspace cursor traversal produces correct content for every workspace.
    /// </summary>
    public void Update(WorkspaceDashboardViewModel vm)
    {
        _vm = vm;
        var innerWidth = FormMinWidth - 12 - 8;

        // Header
        _nameLabel.Text    = vm.Name;
        _desktopChip.Text  = $"Desktop {vm.Desktop}";

        // Subtitle: path width depends on whether iconStyle chip is visible
        _iconStyleChip.Visible = vm.IconStyle is not null;
        if (vm.IconStyle is not null)
            _iconStyleChip.Text = vm.IconStyle;
        _pathLabel.Width = vm.IconStyle is not null ? 340 : innerWidth;
        _pathLabel.Text  = vm.WorkspacePath;

        // Activity
        _activityLabel.Text = vm.ActivityText;

        // Git row — toggle visibility and rebuild chips
        SetRowVisible(RowGit, vm.Git is not null, GitRowHeight);
        UpdateGitChips(vm.Git);
    }

    // ---- Private helpers ----

    /// <summary>
    /// Shows or hides an optional TableLayoutPanel row by toggling its absolute height.
    /// Zero collapses the row; the given height restores it.
    /// </summary>
    private void SetRowVisible(int rowIndex, bool visible, int height)
    {
        _tableLayout.RowStyles[rowIndex] = new RowStyle(SizeType.Absolute, visible ? height : 0f);
    }

    /// <summary>
    /// Clears and rebuilds the git chip controls inside <see cref="_gitRow"/>.
    /// When <paramref name="git"/> is null, the row is already collapsed by
    /// <see cref="SetRowVisible"/> — this method simply disposes any stale chips.
    /// </summary>
    private void UpdateGitChips(GitInfo? git)
    {
        foreach (Control c in _gitRow.Controls)
            c.Dispose();
        _gitRow.Controls.Clear();

        if (git is null)
            return;

        // Branch chip (always when Git is non-null)
        _gitRow.Controls.Add(MakeChip($"⎇ {git.Branch}", ImrdyPalette.FgSecondary));

        // DirtyCount chip (always when Git is non-null)
        _gitRow.Controls.Add(MakeChip($"+{git.DirtyCount}", ImrdyPalette.FgSecondary));

        // Ahead chip (only when > 0)
        if (git.Ahead > 0)
            _gitRow.Controls.Add(MakeChip($"↑{git.Ahead}", ImrdyPalette.FgSecondary));

        // Behind chip (only when > 0)
        if (git.Behind > 0)
            _gitRow.Controls.Add(MakeChip($"↓{git.Behind}", ImrdyPalette.FgSecondary));
    }

    // ---- Static helpers ----

    private static Label MakeChip(string text, Color foreColor)
        => new()
        {
            Text      = text,
            Font      = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = foreColor,
            BackColor = Color.FromArgb(15, 255, 255, 255),
            AutoSize  = true,
            Padding   = new Padding(5, 1, 5, 1),
            Margin    = new Padding(0, 0, 6, 0),
        };
}

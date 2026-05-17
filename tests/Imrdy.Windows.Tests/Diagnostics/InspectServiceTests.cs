using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using FluentAssertions;
using Imrdy.Core;
using Imrdy.Core.Diagnostics;
using Imrdy.Core.Display;
using Imrdy.Windows.Dashboard;
using Imrdy.Windows.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Imrdy.Windows.Tests.Diagnostics;

/// <summary>
/// Unit tests for <see cref="InspectService.Walk"/>. Each test builds a synthetic
/// <see cref="Form"/>, shows it offscreen so AutoSize and layout fire, walks it, and
/// asserts structural invariants. The STA requirement is satisfied by running each test
/// on a dedicated STA <see cref="Thread"/>.
/// </summary>
public class InspectServiceTests
{
    // ---- STA harness ----

    /// <summary>
    /// Shows <paramref name="form"/> offscreen, drains pending events, lays it out,
    /// walks the tree, then hides and disposes the form.
    /// </summary>
    private static (FormGeometry Geom, IReadOnlyList<LayoutNode> Tree) WalkOffscreen(Form form)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.Show();
        try
        {
            Application.DoEvents();
            form.PerformLayout();
            return InspectService.Walk(form, regionRadius: 14);
        }
        finally
        {
            form.Hide();
            form.Dispose();
        }
    }

    /// <summary>
    /// Runs <paramref name="testBody"/> on a fresh STA thread and re-raises any exception
    /// on the calling (xunit MTA) thread.
    /// </summary>
    private static void RunOnSta(Action testBody)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try { testBody(); }
            catch (Exception ex) { threadEx = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
            throw new InvalidOperationException($"STA thread threw: {threadEx.Message}", threadEx);
    }

    // ---- Fixture path helper ----

    private static string FixturePath(string name) => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "tests", "fixtures", "dashboards", name));

    // ---- Tests ----

    [Fact]
    public void Walker_EmptyForm_ProducesOneNode()
    {
        RunOnSta(() =>
        {
            var form = new Form { Width = 200, Height = 100 };
            var (geom, tree) = WalkOffscreen(form);

            tree.Should().HaveCount(1, "the form itself is node 0 with no children");
            tree[0].Type.Should().Be("Form");
            tree[0].ChildIndexes.Should().BeEmpty();

            geom.FormWidth.Should().Be(200);
            geom.RegionRadius.Should().Be(14);
        });
    }

    [Fact]
    public void Walker_FormWithTwoControls_ProducesThreeNodes()
    {
        RunOnSta(() =>
        {
            var form = new Form { Width = 300, Height = 200 };
            form.Controls.Add(new Label { Text = "A", Name = "labelA" });
            form.Controls.Add(new Label { Text = "B", Name = "labelB" });

            var (_, tree) = WalkOffscreen(form);

            tree.Should().HaveCount(3, "form + 2 labels");
            tree[0].ChildIndexes.Should().HaveCount(2, "form owns both labels");
            tree[1].Type.Should().Be("Label");
            tree[2].Type.Should().Be("Label");
        });
    }

    [Fact]
    public void Walker_NestedPanelAndLabel_ProducesThreeNodesWithCorrectIndexes()
    {
        RunOnSta(() =>
        {
            var label = new Label { Text = "Inner", Name = "innerLabel" };
            var panel = new Panel { Name = "outerPanel" };
            panel.Controls.Add(label);

            var form = new Form { Width = 300, Height = 200 };
            form.Controls.Add(panel);

            var (_, tree) = WalkOffscreen(form);

            // form(0) → panel(1) → label(2)
            tree.Should().HaveCount(3);
            tree[0].ChildIndexes.Should().ContainSingle(i => i == 1, "form → panel");
            tree[1].Type.Should().Be("Panel");
            tree[1].ChildIndexes.Should().ContainSingle(i => i == 2, "panel → label");
            tree[2].Type.Should().Be("Label");
            tree[2].Name.Should().Be("innerLabel");
        });
    }

    [Fact]
    public void Walker_TableLayoutPanel_DetailsCarryRowKeys()
    {
        RunOnSta(() =>
        {
            var tlp = new TableLayoutPanel { Name = "tlp", RowCount = 3, ColumnCount = 1 };
            tlp.RowStyles.Clear();
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 80f));

            var form = new Form { Width = 300, Height = 250 };
            form.Controls.Add(tlp);

            var (_, tree) = WalkOffscreen(form);

            var tlpNode = tree.First(n => n.Type == "TableLayoutPanel");
            tlpNode.Details.Should().ContainKey("row[0]", "row heights must be recorded");
            tlpNode.Details.Should().ContainKey("row[1]");
            tlpNode.Details.Should().ContainKey("row[2]");
        });
    }

    [Fact]
    public void Walker_HiddenControl_IsIncludedWithVisibleFalse()
    {
        RunOnSta(() =>
        {
            var hidden = new Label { Text = "Hidden", Name = "hiddenLabel", Visible = false };
            var form = new Form { Width = 200, Height = 100 };
            form.Controls.Add(hidden);

            var (_, tree) = WalkOffscreen(form);

            var hiddenNode = tree.FirstOrDefault(n => n.Name == "hiddenLabel");
            hiddenNode.Should().NotBeNull("hidden controls must appear in the tree");
            hiddenNode!.Visible.Should().BeFalse("Visible=false must be carried through");
        });
    }

    [Fact]
    public void Walker_LongText_IsTruncatedTo200Chars()
    {
        RunOnSta(() =>
        {
            var longText = new string('X', 300);
            var label = new Label { Text = longText, Name = "longLabel", AutoSize = false, Width = 200, Height = 30 };
            var form = new Form { Width = 400, Height = 200 };
            form.Controls.Add(label);

            var (_, tree) = WalkOffscreen(form);

            var labelNode = tree.First(n => n.Name == "longLabel");
            labelNode.Text.Length.Should().Be(200, "truncated to 197 chars + '...'");
            labelNode.Text.Should().EndWith("...");
        });
    }

    [Fact]
    public void Walker_TransparentAndEmptyColors_EncodedAsTransparent()
    {
        RunOnSta(() =>
        {
            var label = new Label
            {
                Text = "test",
                Name = "transparentLabel",
                BackColor = Color.Transparent,
            };
            var form = new Form { Width = 200, Height = 100 };
            form.Controls.Add(label);

            var (_, tree) = WalkOffscreen(form);

            var node = tree.First(n => n.Name == "transparentLabel");
            node.BackColor.Should().Be("transparent");
        });
    }

    [Fact]
    public void Walker_Walks_Real_DashboardForm()
    {
        // Verifies the ≥30 LayoutNode invariant for a fully-populated DashboardForm.
        // Uses direct JsonSerializer.Deserialize (not LiveDashboardVmBuilder, which is step 6+).
        var fixturePath = FixturePath("aged-done.json");
        File.Exists(fixturePath).Should().BeTrue($"fixture must exist at '{fixturePath}'");

        var bytes = File.ReadAllBytes(fixturePath);
        var vm = JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.DashboardViewModel);
        vm.Should().NotBeNull("aged-done.json must deserialize successfully");

        IReadOnlyList<LayoutNode>? tree = null;
        RunOnSta(() =>
        {
            var form = new SessionDashboardForm(vm!, desktopManager: null, NullLoggerFactory.Instance, isPinned: true, isPreviewMode: false);
            (_, tree) = WalkOffscreen(form); // WalkOffscreen disposes the form in its finally block
        });

        tree.Should().NotBeNull();
        tree!.Count.Should().BeGreaterThanOrEqualTo(30,
            "a fully populated DashboardForm must have at least 30 control nodes");
    }
}

using System.Runtime.InteropServices;
using BootScanner.Core.Models;

namespace BootScanner.App;

internal static class NativeCue
{
    private const int EM_SETCUEBANNER = 0x1501;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    public static void SetCue(TextBox box, string cue, bool showWhenFocused = true)
    {
        if (box.IsHandleCreated)
            SendMessage(box.Handle, EM_SETCUEBANNER, showWhenFocused ? 1 : 0, cue);
        else
            box.HandleCreated += (_, _) => SendMessage(box.Handle, EM_SETCUEBANNER, showWhenFocused ? 1 : 0, cue);
    }
}

internal sealed class PrimaryButton : Button
{
    public PrimaryButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 84, 150);
        FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 70, 128);
        BackColor = UiTheme.Primary;
        ForeColor = UiTheme.PrimaryText;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
    }
}

internal sealed class SectionHeader : Panel
{
    private readonly Label _title;

    public SectionHeader()
    {
        Height = 28;
        Dock = DockStyle.Top;
        BackColor = UiTheme.HeaderBack;
        _title = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 8, 0),
            Font = UiTheme.HeaderFont,
            ForeColor = UiTheme.HeaderText,
            BackColor = UiTheme.HeaderBack
        };
        Controls.Add(_title);
    }

    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    public void AddRight(Control control)
    {
        control.Dock = DockStyle.Right;
        Controls.Add(control);
        control.BringToFront();
        _title.SendToBack();
    }
}

internal sealed class StatusListBox : ListBox
{
    public StatusListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 24;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        BackColor = Color.White;
        Font = UiTheme.UiFont;
        IntegralHeight = false;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        e.Graphics.FillRectangle(Brushes.White, e.Bounds);
        if (e.Index < 0 || e.Index >= Items.Count)
            return;

        if (Items[e.Index] is not ParseStatusEntry entry)
        {
            TextRenderer.DrawText(e.Graphics, Items[e.Index]?.ToString() ?? "", Font, e.Bounds, UiTheme.Text);
            return;
        }

        var color = entry.Outcome switch
        {
            ParseOutcome.Parsed => UiTheme.StatusParsed,
            ParseOutcome.Recognized => UiTheme.StatusParsed,
            ParseOutcome.Skipped => UiTheme.StatusSkipped,
            ParseOutcome.Unavailable => UiTheme.StatusSkipped,
            ParseOutcome.Unknown => UiTheme.Hint,
            _ => UiTheme.StatusFailed
        };

        var cy = e.Bounds.Y + e.Bounds.Height / 2;
        var cx = e.Bounds.X + 14;
        using var brush = new SolidBrush(color);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.FillEllipse(brush, cx - 5, cy - 5, 10, 10);

        var textRect = new Rectangle(e.Bounds.X + 26, e.Bounds.Y, e.Bounds.Width - 32, e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            entry.DisplayText,
            Font,
            textRect,
            UiTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>TreeView with dotted lines and a light-blue selection, matching the v1 sketch.</summary>
internal sealed class DottedTreeView : TreeView
{
    public DottedTreeView()
    {
        DrawMode = TreeViewDrawMode.OwnerDrawAll;
        ShowLines = false;
        ShowPlusMinus = false;
        ShowRootLines = false;
        HideSelection = false;
        FullRowSelect = true;
        ItemHeight = 22;
        Indent = 19;
        BorderStyle = BorderStyle.None;
        BackColor = Color.White;
        Font = UiTheme.UiFont;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Disable the default highlight so our owner-draw selection is visible even when unfocused.
        TrySetExplorerTheme();
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    private void TrySetExplorerTheme()
    {
        try { SetWindowTheme(Handle, "explorer", null); } catch { /* ignore */ }
    }

    protected override void OnDrawNode(DrawTreeNodeEventArgs e)
    {
        if (e.Node is null)
            return;

        var bounds = new Rectangle(0, e.Bounds.Y, ClientSize.Width, e.Bounds.Height);
        var selected = (e.State & TreeNodeStates.Selected) != 0;
        using (var back = new SolidBrush(selected ? UiTheme.Selection : Color.White))
            e.Graphics.FillRectangle(back, bounds);

        var node = e.Node;
        var level = node.Level;
        var textX = 8 + level * Indent + 16;
        var midY = e.Bounds.Y + e.Bounds.Height / 2;
        var glyphX = 8 + level * Indent;

        using var pen = new Pen(UiTheme.TreeLine, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
        // Vertical ancestors
        var ancestor = node.Parent;
        var walk = node;
        for (var l = level - 1; l >= 0; l--)
        {
            if (ancestor is null)
                break;
            if (ancestor.NextNode is not null)
            {
                var ax = 8 + l * Indent + 5;
                e.Graphics.DrawLine(pen, ax, e.Bounds.Y, ax, e.Bounds.Bottom);
            }
            walk = ancestor;
            ancestor = ancestor.Parent;
        }

        // Elbow to this node
        var elbowX = glyphX + 5;
        if (level > 0)
        {
            var startY = e.Bounds.Y;
            e.Graphics.DrawLine(pen, elbowX, startY, elbowX, midY);
            e.Graphics.DrawLine(pen, elbowX, midY, glyphX + 14, midY);
        }

        // Expand / collapse box
        if (node.Nodes.Count > 0)
        {
            var box = new Rectangle(glyphX, midY - 5, 10, 10);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            using var boxPen = new Pen(Color.FromArgb(120, 120, 120));
            e.Graphics.FillRectangle(Brushes.White, box);
            e.Graphics.DrawRectangle(boxPen, box);
            var cx = box.X + 5;
            var cy = box.Y + 5;
            e.Graphics.DrawLine(boxPen, box.X + 2, cy, box.Right - 2, cy);
            if (!node.IsExpanded)
                e.Graphics.DrawLine(boxPen, cx, box.Y + 2, cx, box.Bottom - 2);
        }

        var textRect = new Rectangle(textX + 2, e.Bounds.Y, Math.Max(0, ClientSize.Width - textX - 6), e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            node.Text,
            Font,
            textRect,
            UiTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var node = GetNodeAt(e.X, e.Y);
        if (node is not null)
        {
            var glyphX = 8 + node.Level * Indent;
            if (e.X >= glyphX - 2 && e.X <= glyphX + 14 && node.Nodes.Count > 0)
            {
                node.Toggle();
                SelectedNode = node;
                return;
            }
            SelectedNode = node;
        }
        base.OnMouseDown(e);
    }
}

using BootScanner.Core.Export;
using BootScanner.Core.Models;
using BootScanner.Core.Parsing;

namespace BootScanner.App;

public sealed class MainForm : Form
{
    private readonly BootScanService _scanner = new();
    private BootScanResult? _result;
    private IReadOnlyList<SymbolEntry> _allSymbols = Array.Empty<SymbolEntry>();

    private readonly TextBox _pathBox;
    private readonly PrimaryButton _openButton;
    private readonly Button _exportButton;
    private readonly DottedTreeView _tree;
    private readonly DataGridView _grid;
    private readonly TextBox _searchBox;
    private readonly StatusListBox _status;
    private readonly Label _hint;
    private readonly Panel _toolbar;
    private bool _splitterInitialized;
    private CancellationTokenSource? _scanCancellation;
    private long _scanRequestId;

    public MainForm()
    {
        Text = BootScanner.Core.AppInfo.TitleWithVersion;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);
        Size = new Size(1120, 740);
        BackColor = Color.White;
        Font = UiTheme.UiFont;
        ForeColor = UiTheme.Text;
        DoubleBuffered = true;
        AllowDrop = true;
        KeyPreview = true;
        Padding = new Padding(10, 10, 10, 10);

        _openButton = new PrimaryButton
        {
            Text = "打开目录",
            Size = new Size(88, 28),
            Font = UiTheme.UiFont,
            TabIndex = 0
        };
        _openButton.Click += (_, _) =>
        {
            if (_scanCancellation is not null) _scanCancellation.Cancel();
            else BrowseAndOpen();
        };

        _exportButton = new Button
        {
            Text = "导出 CSV",
            Size = new Size(88, 28),
            Font = UiTheme.UiFont,
            FlatStyle = FlatStyle.System,
            Enabled = false,
            TabIndex = 1
        };
        _exportButton.Click += (_, _) => ExportCsv();

        _pathBox = new TextBox
        {
            Font = UiTheme.UiFont,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            TabIndex = 2
        };
        _pathBox.KeyDown += PathBoxOnKeyDown;
        NativeCue.SetCue(_pathBox, @"C:\TwinCAT\3.1\Boot", showWhenFocused: true);

        _hint = new Label
        {
            Text = "只读 · 未安装 TwinCAT · " + BootScanner.Core.AppInfo.Version,
            AutoSize = true,
            Font = UiTheme.HintFont,
            ForeColor = UiTheme.Hint,
            TextAlign = ContentAlignment.MiddleRight
        };

        _toolbar = new Panel
        {
            Height = 36,
            Dock = DockStyle.Top,
            BackColor = Color.White
        };
        _toolbar.Resize += (_, _) => LayoutToolbar(_toolbar, _openButton, _exportButton, _pathBox, _hint);
        _toolbar.Controls.Add(_openButton);
        _toolbar.Controls.Add(_exportButton);
        _toolbar.Controls.Add(_pathBox);
        _toolbar.Controls.Add(_hint);
        LayoutToolbar(_toolbar, _openButton, _exportButton, _pathBox, _hint);

        // Left: hardware topology
        var left = CreateBorderedPanel();
        var leftHeader = new SectionHeader { Title = "硬件拓扑" };
        _tree = new DottedTreeView { Dock = DockStyle.Fill, ShowNodeToolTips = true };
        left.Controls.Add(_tree);
        left.Controls.Add(leftHeader);

        // Right: variable table + search
        var right = CreateBorderedPanel();
        var rightHeader = new SectionHeader { Title = "暴露变量" };
        _searchBox = new TextBox
        {
            Width = 210,
            Height = 22,
            Font = UiTheme.UiFont,
            Margin = new Padding(0),
            BorderStyle = BorderStyle.FixedSingle
        };
        NativeCue.SetCue(_searchBox, "搜索变量名 / 类型", showWhenFocused: true);
        _searchBox.TextChanged += (_, _) => ApplyFilter();
        var searchHost = new Panel
        {
            Width = 224,
            Height = 28,
            BackColor = UiTheme.HeaderBack,
            Padding = new Padding(0, 3, 8, 3)
        };
        _searchBox.Dock = DockStyle.Fill;
        searchHost.Controls.Add(_searchBox);
        rightHeader.AddRight(searchHost);

        _grid = CreateGrid();
        right.Controls.Add(_grid);
        right.Controls.Add(rightHeader);

        // Min sizes and SplitterDistance cannot be applied until the control has a real width;
        // setting them in the constructor throws InvalidOperationException on some DPI/size paths.
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterWidth = 8,
            BackColor = Color.White
        };
        split.Panel1.Padding = new Padding(0, 0, 4, 0);
        split.Panel2.Padding = new Padding(4, 0, 0, 0);
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);

        var mid = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(0, 6, 0, 6) };
        mid.Controls.Add(split);

        // Bottom: parse status
        var bottom = CreateBorderedPanel();
        bottom.Dock = DockStyle.Bottom;
        bottom.Height = 148;
        var bottomHeader = new SectionHeader { Title = "文件解析状态" };
        _status = new StatusListBox { Dock = DockStyle.Fill };
        bottom.Controls.Add(_status);
        bottom.Controls.Add(bottomHeader);

        Controls.Add(mid);
        Controls.Add(bottom);
        Controls.Add(_toolbar);

        var legal = new LinkLabel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UiTheme.UiFont,
            ForeColor = UiTheme.Hint,
            Text = BootScanner.Core.LegalNotice.Footer + "  " + BootScanner.Core.LegalNotice.LinkText,
            AccessibleName = "免责声明"
        };
        legal.LinkArea = new LinkArea(BootScanner.Core.LegalNotice.Footer.Length + 2, BootScanner.Core.LegalNotice.LinkText.Length);
        legal.LinkClicked += (_, _) => ShowLegalNotice();
        Controls.Add(legal);

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        Load += (_, _) => LayoutSplitter(split);
        split.SizeChanged += (_, _) => LayoutSplitter(split);
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && _scanCancellation is not null)
            {
                _scanCancellation.Cancel();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        FormClosing += (_, _) => _scanCancellation?.Cancel();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Program.TryEnableDarkTitleBar(Handle);
    }

    private void ShowLegalNotice()
    {
        using var dialog = new Form
        {
            Text = "使用说明与免责声明",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(760, 620),
            MinimumSize = new Size(500, 400),
            Font = UiTheme.UiFont,
            ShowInTaskbar = false,
            Padding = new Padding(12)
        };
        var text = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, WordWrap = true,
            Text = BootScanner.Core.LegalNotice.FullText.Replace("\n", Environment.NewLine),
            BackColor = Color.White, Font = UiTheme.UiFont
        };
        var close = new Button { Text = "关闭", Dock = DockStyle.Bottom, Height = 32, DialogResult = DialogResult.Cancel };
        var licenses = new Button { Text = "查看软件许可与第三方声明（已内置）", Dock = DockStyle.Bottom, Height = 32 };
        licenses.Click += (_, _) => ShowLicenseNotices(dialog);
        dialog.Controls.Add(text);
        dialog.Controls.Add(licenses);
        dialog.Controls.Add(close);
        dialog.CancelButton = close;
        dialog.ShowDialog(this);
    }

    private static void ShowLicenseNotices(IWin32Window owner)
    {
        var assembly = typeof(MainForm).Assembly;
        var parts = new List<string>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("BootScanner.Licenses.", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var reader = new StreamReader(stream);
            parts.Add(name + Environment.NewLine + reader.ReadToEnd());
        }
        using var dialog = new Form
        {
            Text = "软件许可与第三方声明", Size = new Size(800, 650), MinimumSize = new Size(500, 400),
            StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, Padding = new Padding(12)
        };
        var close = new Button { Text = "关闭", Dock = DockStyle.Bottom, Height = 32, DialogResult = DialogResult.Cancel };
        dialog.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
            WordWrap = false, Font = UiTheme.UiFont, BackColor = Color.White,
            Text = string.Join(Environment.NewLine + Environment.NewLine, parts)
        });
        dialog.Controls.Add(close);
        dialog.CancelButton = close;
        dialog.ShowDialog(owner);
    }

    private static void LayoutToolbar(Panel toolbar, Button open, Button export, TextBox path, Label hint)
    {
        const int y = 4;
        open.Location = new Point(0, y);
        export.Location = new Point(open.Right + 8, y);
        hint.Location = new Point(Math.Max(export.Right + 16, toolbar.Width - hint.PreferredWidth), y + 5);
        var pathLeft = export.Right + 8;
        var pathRight = hint.Left - 12;
        path.Location = new Point(pathLeft, y + 2);
        path.Width = Math.Max(80, pathRight - pathLeft);
        path.Height = 24;
    }


    private void LayoutSplitter(SplitContainer split)
    {
        if (split.Width <= 0)
            return;
        try
        {
            const int panel1Min = 200;
            const int panel2Min = 320;
            var splitter = Math.Max(1, split.SplitterWidth);
            var maxMin = Math.Max(0, split.Width - splitter);
            // If the window is narrower than both minima, shrink them so the setter cannot throw.
            var p1 = Math.Min(panel1Min, Math.Max(0, maxMin / 2));
            var p2 = Math.Min(panel2Min, Math.Max(0, maxMin - p1));
            split.Panel1MinSize = 0;
            split.Panel2MinSize = 0;
            var min = p1;
            var max = Math.Max(min, split.Width - splitter - p2);
            var desired = _splitterInitialized
                ? Math.Clamp(split.SplitterDistance, min, max)
                : Math.Clamp((int)(split.Width * 0.34), min, max);
            split.SplitterDistance = desired;
            split.Panel1MinSize = p1;
            split.Panel2MinSize = p2;
            if (split.Width >= panel1Min + panel2Min + splitter)
                _splitterInitialized = true;
        }
        catch
        {
            // Never let splitter layout take down the window.
        }
    }

    private static Panel CreateBorderedPanel()
    {
        return new DoubleBufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(1),
            BorderColor = UiTheme.Border
        };
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 26,
            RowTemplate = { Height = 22 },
            EnableHeadersVisualStyles = false,
            GridColor = UiTheme.GridLine,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            Font = UiTheme.UiFont
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.GridHeader;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.HeaderText;
        grid.ColumnHeadersDefaultCellStyle.Font = UiTheme.UiFont;
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 4, 0);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiTheme.GridHeader;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiTheme.HeaderText;
        grid.DefaultCellStyle.BackColor = Color.White;
        grid.DefaultCellStyle.ForeColor = UiTheme.Text;
        grid.DefaultCellStyle.SelectionBackColor = UiTheme.Selection;
        grid.DefaultCellStyle.SelectionForeColor = UiTheme.Text;
        grid.DefaultCellStyle.Padding = new Padding(6, 0, 4, 0);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = UiTheme.Selection;
        grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = UiTheme.Text;
        grid.RowHeadersDefaultCellStyle.SelectionBackColor = UiTheme.Selection;

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "名称",
            FillWeight = 46,
            SortMode = DataGridViewColumnSortMode.Automatic
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Type",
            HeaderText = "类型",
            FillWeight = 18
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Direction",
            HeaderText = "方向",
            FillWeight = 12
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Address",
            HeaderText = "地址 / 偏移",
            FillWeight = 24
        });

        return grid;
    }

    private void BrowseAndOpen()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "选择 TwinCAT Boot 目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (!string.IsNullOrWhiteSpace(_pathBox.Text) && Directory.Exists(_pathBox.Text.Trim()))
            dlg.SelectedPath = _pathBox.Text.Trim();
        else
            dlg.SelectedPath = GuessDefaultPath();

        if (dlg.ShowDialog(this) == DialogResult.OK)
            OpenPath(dlg.SelectedPath);
    }

    private static string GuessDefaultPath()
    {
        foreach (var candidate in new[]
                 {
                     @"C:\TwinCAT\3.1\Boot",
                     @"C:\ProgramData\Beckhoff\TwinCAT\3.1\Boot",
                     @"C:\TwinCAT\Boot"
                 })
        {
            if (Directory.Exists(candidate))
                return candidate;
        }
        return @"C:\TwinCAT\3.1\Boot";
    }

    private void PathBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            var text = _pathBox.Text.Trim().Trim('"');
            if (!string.IsNullOrEmpty(text))
                OpenPath(text);
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;
        OpenPath(files[0]);
    }

    private async void OpenPath(string path)
    {
        _scanCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;
        var requestId = ++_scanRequestId;
        _exportButton.Enabled = false;
        _result = null;
        _allSymbols = Array.Empty<SymbolEntry>();
        _tree.Nodes.Clear();
        _grid.Rows.Clear();
        _status.Items.Clear();
        _pathBox.Text = path;
        _hint.Text = "正在扫描 · Esc 取消";
        _openButton.Text = "取消扫描";
        LayoutToolbar(_toolbar, _openButton, _exportButton, _pathBox, _hint);
        try
        {
            var result = await Task.Run(() => _scanner.Scan(path, cancellation.Token), cancellation.Token);
            if (IsDisposed || Disposing || requestId != _scanRequestId || cancellation.IsCancellationRequested) return;
            ApplyResult(result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!IsDisposed && !Disposing && requestId == _scanRequestId)
                _hint.Text = "扫描已取消 · " + BootScanner.Core.AppInfo.Version;
        }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing || requestId != _scanRequestId) return;
            _hint.Text = "扫描失败 · " + BootScanner.Core.AppInfo.Version;
            MessageBox.Show(this, "扫描失败：" + ex.Message, "Boot 查看器",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed && !Disposing && requestId == _scanRequestId)
            {
                _scanCancellation = null;
                _openButton.Text = "打开目录";
                LayoutToolbar(_toolbar, _openButton, _exportButton, _pathBox, _hint);
            }
        }
    }

    private void ApplyResult(BootScanResult result)
    {
        _result = result;
        _allSymbols = result.Symbols;
        _pathBox.Text = string.IsNullOrEmpty(result.BootDirectory) ? result.RequestedPath ?? "" : result.BootDirectory;
        _exportButton.Enabled = result.HasContent || result.Status.Count > 0;

        if (result.Kind is { } kind && !string.IsNullOrEmpty(result.BootDirectory) && Directory.Exists(result.BootDirectory))
        {
            Text = BootScanner.Core.AppInfo.TitleWithVersion + " — " + kind.WindowTitleSuffix;
            _hint.Text = kind.ToolbarHint + " · " + BootScanner.Core.AppInfo.Version;
        }
        else
        {
            Text = BootScanner.Core.AppInfo.TitleWithVersion;
            _hint.Text = "只读 · 未安装 TwinCAT · " + BootScanner.Core.AppInfo.Version;
        }
        LayoutToolbar(_toolbar, _openButton, _exportButton, _pathBox, _hint);

        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        foreach (var root in result.Topology)
            _tree.Nodes.Add(BuildNode(root));
        _tree.ExpandAll();
        if (_tree.Nodes.Count > 0)
            _tree.TopNode = _tree.Nodes[0];
        _tree.EndUpdate();

        ApplyFilter();

        _status.BeginUpdate();
        _status.Items.Clear();
        foreach (var line in result.Status)
            _status.Items.Add(line);
        _status.EndUpdate();
    }

    private static TreeNode BuildNode(TopologyNode model)
    {
        var node = new TreeNode(model.DisplayText)
        {
            Tag = model,
            ToolTipText = string.Join("\n", new[] { model.Address, model.SourceFile }.Where(s => !string.IsNullOrWhiteSpace(s)))
        };
        foreach (var child in model.Children)
            node.Nodes.Add(BuildNode(child));
        return node;
    }

    private void ApplyFilter()
    {
        var query = _searchBox.Text.Trim();
        IEnumerable<SymbolEntry> list = _allSymbols;
        if (!string.IsNullOrEmpty(query))
        {
            list = list.Where(s =>
                s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.TypeName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.DirectionLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.AddressDisplay.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        _grid.Rows.Clear();
        _grid.SuspendLayout();
        foreach (var s in list)
        {
            var row = _grid.Rows[_grid.Rows.Add(s.Name, s.TypeName, s.DirectionLabel, s.AddressDisplay)];
            foreach (DataGridViewCell cell in row.Cells)
                cell.ToolTipText = string.Join("\n", new[] { s.DataArea, s.SourceFile }.Where(v => !string.IsNullOrWhiteSpace(v)));
        }
        _grid.ClearSelection();
        _grid.ResumeLayout();
    }

    private void ExportCsv()
    {
        if (_result is null)
            return;

        using var dlg = new SaveFileDialog
        {
            Title = "导出 CSV",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            FileName = "boot-scan-variables.csv",
            AddExtension = true,
            DefaultExt = "csv",
            OverwritePrompt = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var (vars, topo) = CsvExporter.Export(_result, dlg.FileName);
            MessageBox.Show(this,
                "已导出：\n" + vars + "\n" + topo + "\n\n文件为 UTF-8（带 BOM），可用 Excel 直接打开。",
                "Boot 查看器",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "Boot 查看器",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

/// <summary>Panel that paints a 1px border in a fixed color.</summary>
internal sealed class DoubleBufferedPanel : Panel
{
    public Color BorderColor { get; set; } = UiTheme.Border;

    public DoubleBufferedPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(BorderColor);
        var r = ClientRectangle;
        r.Width -= 1;
        r.Height -= 1;
        e.Graphics.DrawRectangle(pen, r);
    }
}

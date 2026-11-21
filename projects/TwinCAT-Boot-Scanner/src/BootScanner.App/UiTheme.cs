namespace BootScanner.App;

internal static class UiTheme
{
    public static readonly Color WindowBack = Color.FromArgb(245, 245, 245);
    public static readonly Color PanelBack = Color.White;
    public static readonly Color HeaderBack = Color.FromArgb(232, 237, 242);
    public static readonly Color HeaderText = Color.FromArgb(40, 40, 40);
    public static readonly Color Border = Color.FromArgb(210, 214, 218);
    public static readonly Color Selection = Color.FromArgb(205, 232, 247);
    public static readonly Color SelectionBorder = Color.FromArgb(180, 214, 236);
    public static readonly Color GridHeader = Color.FromArgb(247, 248, 250);
    public static readonly Color GridLine = Color.FromArgb(232, 234, 236);
    public static readonly Color Hint = Color.FromArgb(136, 136, 136);
    public static readonly Color Primary = Color.FromArgb(0, 99, 177);
    public static readonly Color PrimaryText = Color.White;
    public static readonly Color TreeLine = Color.FromArgb(160, 160, 160);
    public static readonly Color StatusParsed = Color.FromArgb(56, 158, 61);
    public static readonly Color StatusSkipped = Color.FromArgb(230, 126, 34);
    public static readonly Color StatusFailed = Color.FromArgb(200, 50, 45);
    public static readonly Color Text = Color.FromArgb(30, 30, 30);

    public static Font UiFont { get; } = CreateFont(9f);
    public static Font UiFontBold { get; } = CreateFont(9f, FontStyle.Bold);
    public static Font HeaderFont { get; } = CreateFont(9.25f, FontStyle.Bold);
    public static Font HintFont { get; } = CreateFont(9f);

    public static Font CreateFont(float size, FontStyle style = FontStyle.Regular)
    {
        foreach (var name in new[] { "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑", "Segoe UI", "Sans Serif" })
        {
            try
            {
                var f = new Font(name, size, style, GraphicsUnit.Point);
                if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    name is "Sans Serif" or "Segoe UI")
                    return f;
                f.Dispose();
            }
            catch
            {
                // try next
            }
        }
        return new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, size, style);
    }
}

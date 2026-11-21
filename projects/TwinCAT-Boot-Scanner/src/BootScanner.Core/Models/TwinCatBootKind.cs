namespace BootScanner.Core.Models;

/// <summary>Detected TwinCAT generation of a Boot folder. Conservative: never bluff.</summary>
public enum TwinCatGeneration
{
    TwinCAT2,
    TwinCAT3,
    Conflict,
    Unknown
}

/// <summary>
/// Result of distinguishing TwinCAT 2 vs TwinCAT 3 from path and official companion files.
/// TwinCAT 2 hardware project is CurrentConfig.tsm (or older .wsm), not CurrentConfig.xml.
/// </summary>
public sealed class TwinCatBootKind
{
    public required TwinCatGeneration Generation { get; init; }
    public required string Reason { get; init; }
    /// <summary>Layout note such as "3.1.4026+ ProgramData 布局", only when the path actually matches.</summary>
    public string? LayoutNote { get; init; }
    /// <summary>CurrentConfig.tsm or CurrentConfig.wsm when present at Boot root; never CurrentConfig.xml.</summary>
    public string? HardwareConfigFile { get; init; }

    public IReadOnlyList<string> TwinCat3Evidence { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TwinCat2Evidence { get; init; } = Array.Empty<string>();

    /// <summary>「已识别：TwinCAT 3」 / 「未能明确分辨 TwinCAT 2 / 3」.</summary>
    public string StatusHeadline => Generation switch
    {
        TwinCatGeneration.TwinCAT3 => "已识别：TwinCAT 3",
        TwinCatGeneration.TwinCAT2 => "已识别：TwinCAT 2",
        TwinCatGeneration.Conflict => "已识别：特征冲突，请核对",
        _ => "未能明确分辨 TwinCAT 2 / 3"
    };

    /// <summary>Short suffix for the window title, e.g. "TwinCAT 3".</summary>
    public string WindowTitleSuffix => Generation switch
    {
        TwinCatGeneration.TwinCAT3 => "TwinCAT 3",
        TwinCatGeneration.TwinCAT2 => "TwinCAT 2",
        TwinCatGeneration.Conflict => "特征冲突",
        _ => "未能分辨版本"
    };

    /// <summary>Toolbar hint once a folder is open. Empty state keeps "只读 · 未安装 TwinCAT".</summary>
    public string ToolbarHint
    {
        get
        {
            var core = Generation switch
            {
                TwinCatGeneration.TwinCAT3 => "只读 · TwinCAT 3",
                TwinCatGeneration.TwinCAT2 => string.IsNullOrEmpty(HardwareConfigFile)
                    ? "只读 · TwinCAT 2"
                    : $"只读 · TwinCAT 2（硬件配置：{HardwareConfigFile}）",
                TwinCatGeneration.Conflict => "只读 · 特征冲突，请核对",
                _ => "只读 · 未能明确分辨 TwinCAT 2 / 3"
            };
            return string.IsNullOrEmpty(LayoutNote) ? core : $"{core} · {LayoutNote}";
        }
    }
}

namespace BootScanner.Core.Models;

/// <summary>Outcome of parsing a single file in the Boot folder.</summary>
public enum ParseOutcome
{
    Parsed,
    Recognized,
    Skipped,
    Failed,
    Unknown,
    Unavailable
}

/// <summary>One line in the bottom status pane.</summary>
public sealed class ParseStatusEntry
{
    public required string FileName { get; init; }
    public string? RelativePath { get; init; }
    public required ParseOutcome Outcome { get; init; }
    /// <summary>Short reason or count, without the 已解析/已跳过/失败 prefix.</summary>
    public required string Detail { get; init; }
    /// <summary>Version-identification line: no 已解析/已跳过/失败 prefix.</summary>
    public bool IsHeadline { get; init; }


    public string OutcomeLabel => Outcome switch
    {
        ParseOutcome.Parsed => "已解析",
        ParseOutcome.Recognized => "已识别",
        ParseOutcome.Skipped => "已跳过",
        ParseOutcome.Failed => "失败",
        ParseOutcome.Unknown => "未知类型",
        ParseOutcome.Unavailable => "暂不可用",
        _ => Outcome.ToString()
    };

    /// <summary>Display text as shown in the UI, e.g. "CurrentConfig.xml  已解析 · 1 个主站，4 个端子".</summary>
    public string DisplayText
    {
        get
        {
            if (IsHeadline)
                return string.IsNullOrWhiteSpace(Detail) ? FileName : $"{FileName}  ·  {Detail}";
            if (string.IsNullOrWhiteSpace(Detail))
                return $"{FileName}  {OutcomeLabel}";
            return $"{FileName}  {OutcomeLabel} · {Detail}";
        }
    }
}

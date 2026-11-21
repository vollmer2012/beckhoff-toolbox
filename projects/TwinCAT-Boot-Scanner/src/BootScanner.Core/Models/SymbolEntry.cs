namespace BootScanner.Core.Models;

public enum SymbolDirection
{
    Input,
    Output,
    InOut,
    Internal,
    Unknown
}

/// <summary>One exposed PLC / module symbol from a TMC, TMI, or TPY file.</summary>
public sealed class SymbolEntry
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public SymbolDirection Direction { get; init; } = SymbolDirection.Internal;
    /// <summary>Display value for the address column (IEC like I0.0, or hex offset like 0x00A0).</summary>
    public required string AddressDisplay { get; init; }
    public string? IecAddress { get; init; }
    public int? BitOffset { get; init; }
    public string? SourceFile { get; init; }
    public string? DataArea { get; init; }

    public uint? IndexGroup { get; init; }
    public uint? IndexOffset { get; init; }

    public string DirectionLabel => Direction switch
    {
        SymbolDirection.Input => "输入",
        SymbolDirection.Output => "输出",
        SymbolDirection.InOut => "输入/输出",
        SymbolDirection.Unknown => "未知",
        _ => "内部"
    };

    internal string DedupKey =>
        $"{Name}|{TypeName}|{Direction}|{AddressDisplay}";
}

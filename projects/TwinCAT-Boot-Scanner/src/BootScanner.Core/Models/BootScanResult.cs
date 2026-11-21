namespace BootScanner.Core.Models;

/// <summary>Complete read-only scan of a Boot directory.</summary>
public sealed class BootScanResult
{
    public required string BootDirectory { get; init; }
    public string? RequestedPath { get; init; }
    public IReadOnlyList<TopologyNode> Topology { get; init; } = Array.Empty<TopologyNode>();
    public IReadOnlyList<SymbolEntry> Symbols { get; init; } = Array.Empty<SymbolEntry>();
    public TwinCatBootKind? Kind { get; init; }
    public IReadOnlyList<ParseStatusEntry> Status { get; init; } = Array.Empty<ParseStatusEntry>();

    public bool HasContent => Topology.Count > 0 || Symbols.Count > 0;
}

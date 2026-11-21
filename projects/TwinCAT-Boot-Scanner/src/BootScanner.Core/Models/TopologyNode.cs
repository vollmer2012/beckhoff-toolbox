namespace BootScanner.Core.Models;

public enum TopologyKind
{
    Device,
    Master,
    Box,
    Terminal,
    Cpu,
    System,
    Variable,
    Other
}

/// <summary>One node in the hardware topology tree.</summary>
public sealed class TopologyNode
{
    public required string Name { get; set; }
    public TopologyKind Kind { get; init; } = TopologyKind.Other;
    /// <summary>Order number / type such as EK1100 or EL1008, when known.</summary>
    public string? OrderNumber { get; init; }
    /// <summary>Compact address summary (PhysAddr, AutoInc, AMS NetId, ...).</summary>
    public string? Address { get; init; }
    /// <summary>Relative file and XML/stream location that produced this node.</summary>
    public string? SourceFile { get; set; }
    /// <summary>
    /// PLC symbol names attached only from an explicit CurrentConfig Mappings/Link.
    /// Never guessed from addresses.
    /// </summary>
    public List<string> LinkedSymbols { get; } = new();
    public List<TopologyNode> Children { get; } = new();

    /// <summary>Tree / CSV caption. Linked PLC names are shown on the node or its channel children.</summary>
    public string DisplayText
    {
        get
        {
            if (Kind == TopologyKind.Variable)
                return Name;
            if (LinkedSymbols.Count == 0)
                return Name;
            if (Children.Any(c => c.Kind == TopologyKind.Variable))
                return Name;
            return Name + "    " + string.Join("，", LinkedSymbols);
        }
    }

    public IEnumerable<TopologyNode> EnumerateDepthFirst()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var n in child.EnumerateDepthFirst())
                yield return n;
        }
    }
}

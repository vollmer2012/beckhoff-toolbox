using System.Xml;
using System.Xml.Linq;
using BootScanner.Core.Models;

namespace BootScanner.Core.Parsing;

public sealed class CurrentConfigParseResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string RootElement { get; init; } = "";
    public IReadOnlyList<TopologyNode> Roots { get; init; } = Array.Empty<TopologyNode>();
    /// <summary>System configuration node when at least one documented field is present; otherwise null.</summary>
    public TopologyNode? SystemConfig { get; init; }
    /// <summary>True when a System element exists in the document (even if no listable fields were found).</summary>
    public bool HasSystemSection { get; init; }
    /// <summary>Number of explicit Mappings/Link pairs attached to I/O nodes. Never inferred from addresses.</summary>
    public int MappingLinkCount { get; init; }
    public string Summary { get; init; } = "";
}

/// <summary>
/// Read-only parser for TwinCAT CurrentConfig.xml / TcSmProject / TreeItem / TcConfig documents.
/// </summary>
public sealed partial class CurrentConfigParser
{
    public CurrentConfigParseResult ParseFile(string path)
    {
        try
        {
            var doc = XmlUtil.LoadXDocument(path);
            return ParseDocument(doc);
        }
        catch (XmlException ex)
        {
            var loc = ex.LineNumber > 0 ? $"第 {ex.LineNumber} 行" : "位置未知";
            return Fail($"XML 格式错误（{loc}）：{Sanitize(ex.Message)}");
        }
        catch (Exception ex)
        {
            return Fail($"无法读取：{Sanitize(ex.Message)}");
        }
    }

    public CurrentConfigParseResult ParseDocument(XDocument doc)
    {
        var root = doc.Root;
        if (root is null)
            return Fail("XML 根元素为空");

        var rootName = root.Name.LocalName;
        var ioRoots = new List<TopologyNode>();
        CollectFromContainer(root, ioRoots);
        CollectBootProjectInfo(root, ioRoots);

        var system = BuildSystemConfig(root);
        var hasSystemSection = HasSystemElement(root);
        var mappingCount = ApplyMappings(root, ioRoots);

        var roots = new List<TopologyNode>();
        if (system is not null)
            roots.Add(system);
        roots.AddRange(ioRoots);

        string summary;
        if (ioRoots.Count == 0)
        {
            summary = rootName.Equals("TcBootProject", StringComparison.OrdinalIgnoreCase)
                ? "此文件为 TcBootProject 运行时格式，不含可读的 I/O 设备树"
                : "配置中没有找到 I/O 设备或 TreeItem 节点";
        }
        else
        {
            summary = rootName.Equals("TcBootProject", StringComparison.OrdinalIgnoreCase)
                ? $"ProjectInfo：{ioRoots.Count} 个设备，{ioRoots.SelectMany(n => n.EnumerateDepthFirst()).Count(n => n.Kind == TopologyKind.Box)} 个从站（按文件层级，不推断物理布线）"
                : Summarize(ioRoots);
        }

        foreach (var node in roots.SelectMany(n => n.EnumerateDepthFirst()))
            node.SourceFile ??= "CurrentConfig.xml#" + rootName;

        return new CurrentConfigParseResult
        {
            Success = true,
            RootElement = rootName,
            Roots = roots,
            SystemConfig = system,
            HasSystemSection = hasSystemSection,
            MappingLinkCount = mappingCount,
            Summary = summary
        };
    }

    private static CurrentConfigParseResult Fail(string error) => new()
    {
        Success = false,
        Error = error,
        Summary = error,
        Roots = Array.Empty<TopologyNode>()
    };

    private static void CollectFromContainer(XElement root, List<TopologyNode> roots)
    {
        // Prefer the Io section when present (TcSmProject / TcConfig).
        var ioElements = root.DescendantsLocal("Io").ToList();
        var devices = new List<XElement>();
        if (ioElements.Count > 0)
        {
            foreach (var io in ioElements)
                devices.AddRange(io.ElementsLocal("Device"));
        }
        else
        {
            // Devices that are not buried inside type libraries or mappings.
            devices.AddRange(root.ElementsLocal("Device"));
            var project = root.ElementLocal("Project") ?? root;
            devices.AddRange(project.ElementsLocal("Device"));
        }

        var seenDevices = new HashSet<XElement>();
        foreach (var device in devices.Distinct())
        {
            seenDevices.Add(device);
            roots.Add(ParseDevice(device));
        }

        // TreeItem-based documents (exported XML / some TC2 CurrentConfig layouts).
        foreach (var item in FindTopLevelTreeItems(root, seenDevices))
            roots.Add(ParseTreeItem(item));

    }

    private static IEnumerable<XElement> FindTopLevelTreeItems(XElement root, HashSet<XElement> skipAncestors)
    {
        foreach (var item in root.DescendantsAndSelf().Where(e => e.Name.LocalName.Equals("TreeItem", StringComparison.OrdinalIgnoreCase)))
        {
            var parentItem = item.Parent;
            while (parentItem is not null && !parentItem.Name.LocalName.Equals("TreeItem", StringComparison.OrdinalIgnoreCase))
                parentItem = parentItem.Parent;
            if (parentItem is not null)
                continue; // nested, will be walked from the parent TreeItem

            // Skip TreeItems already covered as children of a Device we parsed.
            if (item.Ancestors().Any(skipAncestors.Contains))
                continue;

            yield return item;
        }
    }



    private static TopologyNode ParseDevice(XElement device)
    {
        var name = device.ChildText("Name") ?? device.Attr("RemoteName") ?? device.Attr("Name") ?? "Device";
        var devType = device.Attr("DevType");
        var addrParts = new List<string>();
        Append(addrParts, "AMS", device.Attr("AmsNetId"));
        Append(addrParts, "Port", device.Attr("AmsPort"));

        var pnp = device.ElementLocal("AddressInfo")?.ElementLocal("Pnp");
        var nic = pnp?.ChildText("DeviceName") ?? pnp?.ChildText("DeviceDesc");
        if (!string.IsNullOrEmpty(nic))
            addrParts.Add(nic);

        var node = new TopologyNode
        {
            Name = name,
            Kind = TopologyKind.Device,
            Address = Join(addrParts)
        };

        var isEtherCat = IsEtherCatDevice(device, devType, name);
        var parentForChildren = node;
        if (isEtherCat)
        {
            var masterAddr = Join(new[]
            {
                string.IsNullOrEmpty(device.Attr("AmsNetId")) ? null : $"AMS {device.Attr("AmsNetId")}",
                string.IsNullOrEmpty(device.Attr("AmsPort")) ? null : $"Port {device.Attr("AmsPort")}"
            }.Where(s => s is not null)!);
            var master = new TopologyNode
            {
                Name = "EtherCAT Master",
                Kind = TopologyKind.Master,
                Address = masterAddr
            };
            node.Children.Add(master);
            parentForChildren = master;
        }

        foreach (var child in device.Elements())
        {
            var ln = child.Name.LocalName;
            if (ln.Equals("Box", StringComparison.OrdinalIgnoreCase) ||
                ln.Equals("Term", StringComparison.OrdinalIgnoreCase))
            {
                parentForChildren.Children.Add(ParseBoxOrTerm(child, isCouplerLevel: true));
            }
            else if (ln.Equals("TreeItem", StringComparison.OrdinalIgnoreCase))
            {
                parentForChildren.Children.Add(ParseTreeItem(child));
            }
            else if (ln.Equals("Device", StringComparison.OrdinalIgnoreCase))
            {
                parentForChildren.Children.Add(ParseDevice(child));
            }
        }

        return node;
    }

    private static bool IsEtherCatDevice(XElement device, string? devType, string name)
    {
        if (devType == "111" || devType == "130")
            return true;
        if (name.Contains("EtherCAT", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RT-Ethernet", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RT Ethernet", StringComparison.OrdinalIgnoreCase))
            return true;
        return device.Elements().Any(e =>
            e.Name.LocalName.Equals("Box", StringComparison.OrdinalIgnoreCase) ||
            e.Name.LocalName.Equals("Term", StringComparison.OrdinalIgnoreCase) ||
            e.Name.LocalName.Equals("EtherCAT", StringComparison.OrdinalIgnoreCase));
    }

    private static TopologyNode ParseBoxOrTerm(XElement el, bool isCouplerLevel)
    {
        var name = el.ChildText("Name") ?? el.Attr("Name") ?? el.Name.LocalName;
        var ether = el.ElementLocal("EtherCAT");
        var info = ether?.ElementLocal("Slave")?.ElementLocal("Info")
                   ?? ether?.ElementLocal("Info");

        var order = FirstNonEmpty(
            info?.ChildText("Type"),
            ether?.Attr("Desc"),
            TryOrderNumberFromLongType(ether?.Attr("Type")),
            TryOrderNumberFromName(name));

        var addrParts = new List<string>();
        var phys = info?.ChildText("PhysAddr");
        var autoInc = info?.ChildText("AutoIncAddr");
        var productRev = info?.ChildText("ProductRevision") ?? ether?.Attr("ProductRevision");
        var fieldbus = el.ElementLocal("BoxDef")?.ChildText("FieldbusAddress")
                       ?? el.ChildText("FieldbusAddress");
        Append(addrParts, "PhysAddr", phys);
        Append(addrParts, "AutoInc", autoInc);
        if (!string.IsNullOrEmpty(fieldbus) && fieldbus != phys)
            Append(addrParts, "Addr", fieldbus);

        var kind = InferBoxKind(el.Name.LocalName, order, isCouplerLevel, name);

        var node = new TopologyNode
        {
            Name = name,
            Kind = kind,
            OrderNumber = order,
            Address = Join(addrParts)
        };

        // Product revision is useful but kept off the address string unless no address exists.
        _ = productRev;

        foreach (var child in el.Elements())
        {
            var ln = child.Name.LocalName;
            if (ln.Equals("Box", StringComparison.OrdinalIgnoreCase) ||
                ln.Equals("Term", StringComparison.OrdinalIgnoreCase))
            {
                node.Children.Add(ParseBoxOrTerm(child, isCouplerLevel: false));
            }
            else if (ln.Equals("TreeItem", StringComparison.OrdinalIgnoreCase))
            {
                node.Children.Add(ParseTreeItem(child));
            }
        }

        return node;
    }

    private static TopologyKind InferBoxKind(string tag, string? order, bool isCouplerLevel, string name)
    {
        if (tag.Equals("Term", StringComparison.OrdinalIgnoreCase))
            return TopologyKind.Terminal;
        if (LooksLikeCoupler(order, name) || isCouplerLevel)
            return TopologyKind.Box;
        return TopologyKind.Terminal;
    }

    private static bool LooksLikeCoupler(string? order, string name)
    {
        var token = order ?? "";
        if (token.StartsWith("EK", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("BK", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("CU", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("EP", StringComparison.OrdinalIgnoreCase) && token.Contains("0000"))
            return true;
        return name.Contains("耦合", StringComparison.Ordinal) ||
               name.Contains("Coupler", StringComparison.OrdinalIgnoreCase);
    }

    private static TopologyNode ParseTreeItem(XElement item)
    {
        var name = item.ChildText("ItemName") ?? item.ChildText("Name") ?? "Item";
        var itemType = item.ChildText("ItemType") ?? item.Attr("ItemType");
        var subType = item.ChildText("ItemSubType") ?? item.Attr("ItemSubType");

        var kind = itemType switch
        {
            "2" => TopologyKind.Device,
            "5" => TopologyKind.Box,
            "6" => TopologyKind.Terminal,
            _ => TopologyKind.Other
        };

        var ether = item.ElementLocal("EtherCAT");
        var info = ether?.ElementLocal("Slave")?.ElementLocal("Info") ?? ether?.ElementLocal("Info");
        var boxDef = item.ElementLocal("BoxDef");
        var order = FirstNonEmpty(
            info?.ChildText("Type"),
            item.ChildText("ItemSubTypeName") is { } subName ? TryOrderNumberFromLongType(subName) : null,
            TryOrderNumberFromName(name));

        var addrParts = new List<string>();
        Append(addrParts, "PhysAddr", info?.ChildText("PhysAddr"));
        Append(addrParts, "AutoInc", info?.ChildText("AutoIncAddr"));
        var fieldbus = boxDef?.ChildText("FieldbusAddress");
        if (!string.IsNullOrEmpty(fieldbus) && fieldbus != info?.ChildText("PhysAddr"))
            Append(addrParts, "Addr", fieldbus);

        var node = new TopologyNode
        {
            Name = name,
            Kind = kind,
            OrderNumber = order,
            Address = Join(addrParts)
        };

        var isEtherCat = subType == "111" ||
                         name.Contains("EtherCAT", StringComparison.OrdinalIgnoreCase) ||
                         kind == TopologyKind.Device && item.ElementsLocal("TreeItem").Any();

        var parentForChildren = node;
        if (kind == TopologyKind.Device && isEtherCat &&
            !name.Equals("EtherCAT Master", StringComparison.OrdinalIgnoreCase))
        {
            var master = new TopologyNode { Name = "EtherCAT Master", Kind = TopologyKind.Master };
            node.Children.Add(master);
            parentForChildren = master;
        }

        foreach (var child in item.ElementsLocal("TreeItem"))
            parentForChildren.Children.Add(ParseTreeItem(child));

        foreach (var child in item.Elements())
        {
            var ln = child.Name.LocalName;
            if (ln.Equals("Box", StringComparison.OrdinalIgnoreCase) ||
                ln.Equals("Term", StringComparison.OrdinalIgnoreCase))
            {
                parentForChildren.Children.Add(ParseBoxOrTerm(child, isCouplerLevel: kind == TopologyKind.Device));
            }
        }

        return node;
    }

    public static string Summarize(IReadOnlyList<TopologyNode> roots)
    {
        int masters = 0, terminals = 0, devices = 0, cpus = 0;
        foreach (var n in roots.SelectMany(r => r.EnumerateDepthFirst()))
        {
            switch (n.Kind)
            {
                case TopologyKind.Master:
                    masters++;
                    break;
                case TopologyKind.Device:
                    devices++;
                    break;
                case TopologyKind.Box:
                case TopologyKind.Terminal:
                    terminals++;
                    break;
                case TopologyKind.Cpu:
                    cpus++;
                    break;
            }
        }

        // A lone EtherCAT device without a synthetic Master child still counts as a master.
        if (masters == 0)
        {
            foreach (var n in roots.SelectMany(r => r.EnumerateDepthFirst()))
            {
                if (n.Kind == TopologyKind.Device &&
                    (n.Name.Contains("EtherCAT", StringComparison.OrdinalIgnoreCase) ||
                     n.Children.Any(c => c.Kind is TopologyKind.Box or TopologyKind.Terminal)))
                {
                    masters++;
                }
            }
        }

        var parts = new List<string>();
        if (masters > 0)
            parts.Add($"{masters} 个主站");
        if (terminals > 0)
            parts.Add($"{terminals} 个端子");
        if (devices > 0 && masters == 0 && terminals == 0)
            parts.Add($"{devices} 个设备");
        if (cpus > 0)
            parts.Add($"{cpus} 个 CPU");
        return parts.Count == 0 ? "已读取配置，但没有识别出设备节点" : string.Join("，", parts);
    }

    private static string? TryOrderNumberFromName(string name)
    {
        // "Term 2 (EL1008)" or "Term 2 EL1008 8通道..." or "EL1008"
        var parts = name.Replace("(", " ").Replace(")", " ").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (LooksLikeOrderNumber(p))
                return TrimOrderNumber(p);
        }
        return null;
    }

    private static string? TryOrderNumberFromLongType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return null;
        var first = type.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
        return LooksLikeOrderNumber(first) ? TrimOrderNumber(first) : null;
    }

    private static bool LooksLikeOrderNumber(string token)
    {
        if (token.Length < 4)
            return false;
        // EK1100, EL1008, EL3102, AX5101, CX9020, CU1128, EP2338-0002, etc.
        var head = token.Split('-')[0];
        if (head.Length < 4)
            return false;
        var letters = 0;
        while (letters < head.Length && char.IsLetter(head[letters]))
            letters++;
        var digits = 0;
        for (var i = letters; i < head.Length && char.IsDigit(head[i]); i++)
            digits++;
        return letters is >= 1 and <= 3 && digits >= 3;
    }

    private static string TrimOrderNumber(string token)
    {
        var cut = token.IndexOf('-');
        // Keep full product like EP2338-0002 if present, otherwise first token.
        return token.Trim();
    }

    private static void Append(List<string> parts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parts.Add($"{label} {value.Trim()}");
    }

    private static string? Join(IEnumerable<string> parts)
    {
        var list = parts.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        return list.Count == 0 ? null : string.Join(", ", list);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string Sanitize(string message)
    {
        // Keep the message short and avoid leaking absolute paths from XmlException internals if possible.
        var first = message.Split('\n')[0].Trim();
        return first.Length > 240 ? first[..240] + "…" : first;
    }
}

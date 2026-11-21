using System.Xml.Linq;
using BootScanner.Core.Models;

namespace BootScanner.Core.Parsing;

public sealed partial class CurrentConfigParser
{
    // Runtime ProjectInfo is distinct from engineering TcSmProject/Io.
    // Numeric Type codes are retained literally, never interpreted as order numbers.
    private static void CollectBootProjectInfo(XElement root, List<TopologyNode> ioRoots)
    {
        if (!root.Name.LocalName.Equals("TcBootProject", StringComparison.OrdinalIgnoreCase)) return;
        var info = root.ElementLocal("ProjectInfo");
        var devices = info?.ElementLocal("Devices");
        if (devices is null) return;
        foreach (var device in devices.ElementsLocal("Device"))
            ioRoots.Add(ParseRuntimeHardware(device, TopologyKind.Device, "ProjectInfo/Devices/Device"));
    }

    private static TopologyNode ParseRuntimeHardware(XElement element, TopologyKind kind, string source)
    {
        var type = element.ChildText("Type");
        var name = element.ChildText("Name") ?? (kind == TopologyKind.Device ? "未命名设备" : "未命名从站");
        if (!string.IsNullOrWhiteSpace(type)) name += "（类型代码 " + type + "）";
        var address = new List<string>();
        var ads = element.ElementLocal("AdsInfo");
        if (ads?.ChildText("NetId") is { Length: > 0 } net) address.Add("AMS NetId " + net);
        if (ads?.ChildText("Port") is { Length: > 0 } port) address.Add("ADS Port " + port);
        if (element.ChildText("FieldbusAddress") is { Length: > 0 } bus) address.Add("FieldbusAddress " + bus);
        var node = new TopologyNode
        {
            Name = name, Kind = kind, Address = string.Join("，", address),
            SourceFile = "CurrentConfig.xml#" + source
        };
        if (element.ElementLocal("Boxes") is { } boxes)
            foreach (var box in boxes.ElementsLocal("Box"))
                node.Children.Add(ParseRuntimeHardware(box, TopologyKind.Box, source + "/Boxes/Box"));
        return node;
    }
}

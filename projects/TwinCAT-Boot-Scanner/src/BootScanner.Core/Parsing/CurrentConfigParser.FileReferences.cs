using System.Xml.Linq;
using BootScanner.Core.Models;

namespace BootScanner.Core.Parsing;

public sealed partial class CurrentConfigParser
{
    private static void AddFileReferences(XElement root, TopologyNode parent)
    {
        var info = root.ElementLocal("ProjectInfo");
        if (info?.ChildText("ConfigurationFile") is { Length: > 0 } configuration)
            parent.Children.Add(new()
            {
                Name = "原组态文件路径（记录值，未访问）：" + configuration,
                Kind = TopologyKind.System,
                SourceFile = "CurrentConfig.xml#ProjectInfo/ConfigurationFile"
            });
        foreach (var plc in info?.ElementLocal("PlcProjects")?.ElementsLocal("PlcProject") ?? Enumerable.Empty<XElement>())
        {
            var node = new TopologyNode { Name = "PLC 工程引用（XML 记录）", Kind = TopologyKind.System };
            var source = "CurrentConfig.xml#ProjectInfo/PlcProjects/PlcProject";
            if ((plc.Attr("Name") ?? plc.ChildText("Name")) is { Length: > 0 } name)
                node.Children.Add(new() { Name = "PLC 工程名：" + name, SourceFile = source + "/Name" });
            AddProjectPaths(plc, node, source);
            if (node.Children.Count > 0) parent.Children.Add(node);
        }
        var project = root.ElementLocal("Project") ?? root;
        foreach (var plc in project.ElementLocal("Plc")?.ElementsLocal("Project") ?? Enumerable.Empty<XElement>())
        {
            var node = new TopologyNode
            {
                Name = "PLC 文件引用：" + (plc.Attr("Name") ?? plc.ChildText("Name") ?? "未记录工程名"),
                Kind = TopologyKind.System
            };
            AddProjectPaths(plc, node, "CurrentConfig.xml#Project/Plc/Project");
            if (node.Children.Count > 0) parent.Children.Add(node);
        }
    }

    private static void AddProjectPaths(XElement plc, TopologyNode parent, string source)
    {
        foreach (var field in new[] { "Path", "ProjectPath", "ProjectFile", "PrjFilePath", "TpyFilePath", "TmcFilePath" })
            if ((plc.Attr(field) ?? plc.ChildText(field)) is { Length: > 0 } path)
                parent.Children.Add(new()
                {
                    Name = field + "（记录路径，未访问）：" + path,
                    SourceFile = source + "/" + field
                });
    }
}

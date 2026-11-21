using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;
using BootScanner.Core.Models;

namespace BootScanner.Core.Parsing;

internal sealed class TsmProjectResult
{
    public ParseOutcome Outcome { get; init; } = ParseOutcome.Recognized;
    public string Detail { get; init; } = "";
    public List<TopologyNode> Roots { get; } = new();
    public List<SymbolEntry> Symbols { get; } = new();
}

/// <summary>Reads only the known, length-prefixed SlaveDeviceInfo XML stream. WSM remains opaque.</summary>
internal static class TsmProjectParser
{
    private const int MaxXmlBytes = 8 * 1024 * 1024;
    private const string Source = "TcSysManager/SlaveDeviceInfo";

    public static TsmProjectResult Parse(string path)
    {
        var inspection = TsmHardwareFile.Inspect(path);
        // Old demo fixtures are header-only markers, not real compound documents.
        if (inspection.Kind != TsmStorageKind.OleStructuredStorage || new FileInfo(path).Length <= 512)
            return new() { Detail = inspection.Detail };
        if (!OperatingSystem.IsWindows())
            return new() { Detail = "TSM 复合文档读取需要 Windows 系统 Structured Storage 接口；此平台仅识别文件类型" };
        try
        {
            if (new FileInfo(path).Length > 256L * 1024 * 1024)
                throw new InvalidDataException("复合文档超过 256 MiB 读取限制");
            using var root = CompoundStorage.OpenRead(path);
            if (!root.TryOpenStorage("TcSysManager", out var storage))
                return new() { Detail = "已读取复合文档目录，未找到支持的 SlaveDeviceInfo XML 流；WSM 配置流暂未解码，不推断内容" };
            using var childStorage = storage!;
            var reference = WsmFileReferences.Read(childStorage, Path.GetFileName(path));
            if (!childStorage.TryOpenStream("SlaveDeviceInfo", out var stream))
            {
                var partial = new TsmProjectResult { Detail = "复合文档已识别；未找到 SlaveDeviceInfo。WSM 主工程未解码，文件引用仅为字符串候选" };
                if (reference is not null) partial.Roots.Add(reference);
                return partial;
            }
            using (stream)
            {
                if (stream!.Length < 4 || stream.Length > MaxXmlBytes)
                    throw new InvalidDataException("SlaveDeviceInfo 流长度无效或超过 8 MiB 限制");
                var bytes = new byte[(int)stream.Length];
                stream.ReadExactly(bytes);
                var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
                if (size != bytes.Length - 4 || size % 2 != 0)
                    throw new InvalidDataException("SlaveDeviceInfo 的 UTF-16 长度前缀不匹配");
                var xml = new UnicodeEncoding(false, false, true).GetString(bytes, 4, (int)size).TrimEnd('\0');
                var doc = XmlUtil.ParseText(xml);
                if (doc.Root?.Name.LocalName != "SlaveDeviceInfo")
                    throw new InvalidDataException("SlaveDeviceInfo 根元素不匹配");
                var result = ParseDescription(doc.Root, Path.GetFileName(path));
                if (reference is not null) result.Roots.Insert(0, reference);
                return result;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Do not echo arbitrary source text, absolute paths or client comments.
            return new() { Outcome = ParseOutcome.Failed, Detail = "TSM 配置描述流读取失败（" + ex.GetType().Name + "）；其余文件仍继续解析" };
        }
    }

    private static TsmProjectResult ParseDescription(XElement root, string fileName)
    {
        var source = fileName + "#" + Source;
        var branch = new TopologyNode { Name = "TSM 从站配置（独立描述，未与硬件树合并）", SourceFile = source };
        var target = root.ElementLocal("TargetDesc");
        if (target?.ChildText("Name") is { Length: > 0 } targetName)
            branch.Children.Add(new() { Name = "目标名称：" + targetName, SourceFile = source + "/TargetDesc/Name" });
        if (target?.ChildText("AmsNetId") is { Length: > 0 } netId)
            branch.Children.Add(new() { Name = "目标 AMS NetId：" + netId, SourceFile = source + "/TargetDesc/AmsNetId" });
        var symbols = new List<SymbolEntry>();
        foreach (var slave in root.ElementLocal("SlaveDevices")?.ElementsLocal("SlaveDevice") ?? Enumerable.Empty<XElement>())
        {
            var name = slave.ChildText("Name") ?? "未命名从站描述";
            var slaveNode = new TopologyNode { Name = name, SourceFile = source + "/SlaveDevices/SlaveDevice" };
            branch.Children.Add(slaveNode);
            foreach (var module in slave.ElementsLocal("ModuleDesc"))
            {
                var moduleName = module.ChildText("Name") ?? "未命名模块";
                var moduleNode = new TopologyNode { Name = moduleName, SourceFile = source + "/ModuleDesc" };
                slaveNode.Children.Add(moduleNode);
                foreach (var flow in new[] { "Producing", "Consuming" })
                foreach (var variable in module.ElementLocal("VarDesc")?.ElementLocal(flow)?.ElementsLocal("Variable") ?? Enumerable.Empty<XElement>())
                {
                    if (variable.ChildText("Name") is not { Length: > 0 } variableName) continue;
                    var properties = new List<string>();
                    if (variable.ChildText("Id") is { } id) properties.Add("Id " + id);
                    if (variable.ChildText("BitSize") is { } bits) properties.Add("BitSize " + bits);
                    var symbolName = "[从站过程数据] " + name + "/" + moduleName + "/" + variableName;
                    symbols.Add(new()
                    {
                        Name = symbolName, TypeName = variable.ChildText("Type") ?? "未声明",
                        Direction = SymbolDirection.Unknown, AddressDisplay = string.Join("，", properties),
                        DataArea = "从站过程数据（非 PLC 符号表）/" + flow,
                        SourceFile = source + "/SlaveDevices/SlaveDevice/ModuleDesc/VarDesc/" + flow
                    });
                    moduleNode.Children.Add(new() { Name = variableName + "（" + flow + "，" + string.Join("，", properties) + "）", SourceFile = source });
                }
            }
        }
        var result = new TsmProjectResult
        {
            Outcome = ParseOutcome.Parsed,
            Detail = $"部分解析：读取 {Source}，{symbols.Count} 个从站过程数据项（非完整 PLC 符号表）；WSM 硬件工程/映射流暂未解码"
        };
        if (branch.Children.Count > 0) result.Roots.Add(branch);
        result.Symbols.AddRange(symbols);
        return result;
    }
}

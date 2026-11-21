using System.Globalization;
using System.Xml.Linq;
using BootScanner.Core.Models;

namespace BootScanner.Core.Parsing;

/// <summary>
/// System configuration nodes and explicit Mappings/Link attachment.
/// CycleTime units (do not invent):
/// - Task/@CycleTime in TcSmProject is in 100 ns units (same as PlcTaskSystemInfo.CycleTime).
///   Sample 100000 → 10 ms. See Beckhoff InfoSys, PlcTaskSystemInfo.
/// - Plc Instance Context/CycleTime in the same document is in nanoseconds:
///   sample 10000000 → 10 ms, matching the associated PlcTask. Public .tsproj files
///   show the same 100× ratio (Task 10000 / Context 1000000 for a 1 ms task).
///   Interpreting Context as 100 ns would yield 1 s here, which contradicts the task.
/// </summary>
public sealed partial class CurrentConfigParser
{
    internal const string SystemConfigTitle = "系统配置";
    internal const string AuxiliarySystemConfigTitle = "系统配置（辅助配置，不是系统管理器硬件工程）";

    internal static bool HasSystemElement(XElement root) =>
        root.DescendantsAndSelf().Any(e => e.Name.LocalName.Equals("System", StringComparison.OrdinalIgnoreCase));

    internal static TopologyNode? BuildSystemConfig(XElement root, string title = SystemConfigTitle)
    {
        var project = root.ElementLocal("Project")
                      ?? (root.Name.LocalName.Equals("Project", StringComparison.OrdinalIgnoreCase) ? root : null);
        var system = project?.ElementLocal("System")
                     ?? root.ElementsLocal("System").FirstOrDefault()
                     ?? project?.ElementsLocal("System").FirstOrDefault();

        var node = new TopologyNode
        {
            Name = title,
            Kind = TopologyKind.System
        };

        var tcVersion = root.Attr("TcVersion") ?? project?.Attr("TcVersion");
        var netId = project?.Attr("TargetNetId");
        var target64 = project?.Attr("Target64Bit");

        if (!string.IsNullOrWhiteSpace(netId))
            node.Children.Add(SysItem("AMS NetId：" + netId.Trim()));
        if (!string.IsNullOrWhiteSpace(target64))
            node.Children.Add(SysItem("64 位目标：" + FormatBool(target64)));
        if (!string.IsNullOrWhiteSpace(tcVersion))
            node.Children.Add(SysItem("TwinCAT 版本：" + tcVersion.Trim()));

        if (system is not null)
        {
            AddRealtimeSettings(system, node);
            if (root.Name.LocalName.Equals("TcBootProject", StringComparison.OrdinalIgnoreCase)
                && system.ChildText("LockedMemSize") is { Length: > 0 } locked)
                node.Children.Add(SysItem("LockedMemSize（原始值）：" + locked));
        }

        AddTasks(system, node);
        AddPlcInstances(project ?? root, system, node);
        AddRoutes(system, node);
        AddFileReferences(root, node);

        return node.Children.Count == 0 ? null : node;
    }

    internal static TopologyNode MarkSystemAsAuxiliary(TopologyNode system)
    {
        system.Name = AuxiliarySystemConfigTitle;
        return system;
    }

    private static void AddRealtimeSettings(XElement system, TopologyNode parent)
    {
        var settings = system.ElementLocal("Settings");
        if (settings is null)
            return;

        var parts = new List<string>();
        var maxCpus = settings.Attr("MaxCpus");
        var nonWin = settings.Attr("NonWinCpus");
        if (!string.IsNullOrWhiteSpace(maxCpus))
            parts.Add("最大 CPU 数 " + maxCpus.Trim());
        if (!string.IsNullOrWhiteSpace(nonWin))
            parts.Add("非 Windows CPU 数 " + nonWin.Trim());

        // Cpu/@CpuId is a real-time core index only. Never invent a CPU / IPC model.
        var cpuIds = settings.ElementsLocal("Cpu")
            .Select(c => c.Attr("CpuId"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .ToList();
        if (cpuIds.Count > 0)
            parts.Add("已分配核 " + string.Join("、", cpuIds));

        if (parts.Count > 0)
            parent.Children.Add(SysItem("实时设置：" + string.Join("，", parts)));
    }

    private static void AddTasks(XElement? system, TopologyNode parent)
    {
        if (system is null)
            return;

        var tasks = system.ElementLocal("Tasks")?.ElementsLocal("Task") ?? Enumerable.Empty<XElement>();
        foreach (var task in tasks)
        {
            var name = task.ChildText("Name") ?? task.Attr("Name") ?? "Task";
            var bits = new List<string>();
            var prio = task.Attr("Priority") ?? task.ChildText("Priority");
            if (!string.IsNullOrWhiteSpace(prio))
                bits.Add("优先级 " + prio.Trim());
            var cycle = FormatTaskCycleMs(task.Attr("CycleTime") ?? task.ChildText("CycleTime"));
            if (!string.IsNullOrWhiteSpace(cycle))
                bits.Add("周期 " + cycle);
            var port = task.Attr("AmsPort") ?? task.ChildText("AmsPort");
            if (!string.IsNullOrWhiteSpace(port))
                bits.Add("AmsPort " + port.Trim());

            var label = bits.Count == 0 ? "任务 " + name : "任务 " + name + "：" + string.Join("，", bits);
            parent.Children.Add(SysItem(label));
        }
    }

    private static void AddPlcInstances(XElement container, XElement? system, TopologyNode parent)
    {
        var plc = container.ElementLocal("Plc");
        if (plc is null)
            return;

        foreach (var project in plc.ElementsLocal("Project"))
        {
            var name = project.Attr("Name") ?? project.ChildText("Name") ?? "PLC";
            var bits = new List<string>();
            var port = project.Attr("AmsPort") ?? project.ChildText("AmsPort");
            if (!string.IsNullOrWhiteSpace(port))
                bits.Add("AmsPort " + port.Trim());

            var context = project.DescendantsLocal("Context").FirstOrDefault();
            var taskName = context?.ChildText("Name");
            if (!string.IsNullOrWhiteSpace(taskName))
                bits.Add("任务 " + taskName.Trim());

            // Prefer the PLC Context cycle (nanoseconds). Fall back to the matching System task (100 ns units).
            var ctxRaw = context?.ChildText("CycleTime") ?? context?.Attr("CycleTime");
            var ctxCycle = FormatContextCycleMs(ctxRaw);
            if (!string.IsNullOrWhiteSpace(ctxCycle))
                ctxCycle += "（原始值 " + ctxRaw + "；暂按 ns，单位待核）";
            if (string.IsNullOrWhiteSpace(ctxCycle) && system is not null && !string.IsNullOrWhiteSpace(taskName))
            {
                var task = system.ElementLocal("Tasks")?.ElementsLocal("Task")
                    .FirstOrDefault(t => string.Equals(t.ChildText("Name") ?? t.Attr("Name"), taskName, StringComparison.OrdinalIgnoreCase));
                ctxCycle = FormatTaskCycleMs(task?.Attr("CycleTime") ?? task?.ChildText("CycleTime"));
            }
            if (!string.IsNullOrWhiteSpace(ctxCycle))
                bits.Add("周期 " + ctxCycle);

            var label = bits.Count == 0 ? "PLC 实例 " + name : "PLC 实例 " + name + "：" + string.Join("，", bits);
            parent.Children.Add(SysItem(label));
        }
    }

    private static void AddRoutes(XElement? system, TopologyNode parent)
    {
        if (system is null)
            return;

        var routes = system.Descendants()
            .Where(e => e.Name.LocalName.Equals("Route", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var route in routes)
        {
            var name = route.ChildText("Name") ?? route.Attr("Name");
            var netId = FirstNonEmpty(route.ChildText("NetId"), route.Attr("NetId"), route.ChildText("Address"), route.Attr("Address"));
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(name))
                bits.Add(name.Trim());
            if (!string.IsNullOrWhiteSpace(netId))
                bits.Add("NetId " + netId.Trim());
            if (bits.Count == 0)
                continue;
            parent.Children.Add(SysItem("路由：" + string.Join("，", bits)));
        }
    }

    /// <summary>Task CycleTime is in 100 ns units → milliseconds.</summary>
    public static string? FormatTaskCycleMs(string? raw)
    {
        if (!TryParseLong(raw, out var ticks) || ticks < 0)
            return null;
        return FormatMs(ticks / 10_000.0);
    }

    /// <summary>Plc Context CycleTime is in nanoseconds → milliseconds.</summary>
    public static string? FormatContextCycleMs(string? raw)
    {
        if (!TryParseLong(raw, out var ns) || ns < 0)
            return null;
        return FormatMs(ns / 1_000_000.0);
    }

    private static string FormatMs(double ms)
    {
        if (Math.Abs(ms - Math.Round(ms)) < 0.0005)
            return ((long)Math.Round(ms)).ToString(CultureInfo.InvariantCulture) + " ms";
        return ms.ToString("0.###", CultureInfo.InvariantCulture) + " ms";
    }

    private static bool TryParseLong(string? raw, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var s = raw.Trim();
        if (s.StartsWith("#x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatBool(string raw)
    {
        var s = raw.Trim();
        if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1")
            return "是";
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "0")
            return "否";
        return s;
    }

    private static TopologyNode SysItem(string name) => new()
    {
        Name = name,
        Kind = TopologyKind.System
    };

    // -------- Mappings / Link --------

    private sealed record IoPlcLink(string IoPath, string PlcPath);

    internal static int ApplyMappings(XElement root, List<TopologyNode> ioRoots)
    {
        if (ioRoots.Count == 0)
            return 0;

        var applied = 0;
        foreach (var link in CollectLinks(root))
        {
            var plcName = PlcVariableDisplayName(link.PlcPath);
            if (string.IsNullOrEmpty(plcName))
                continue;
            if (AttachLink(ioRoots, link.IoPath, plcName))
                applied++;
        }
        return applied;
    }

    internal static List<(string IoPath, string PlcPath)> CollectLinkPairs(XElement root) =>
        CollectLinks(root).Select(l => (l.IoPath, l.PlcPath)).ToList();

    private static List<IoPlcLink> CollectLinks(XElement root)
    {
        var result = new List<IoPlcLink>();

        foreach (var ownerA in root.DescendantsAndSelf().Where(e => Local(e, "OwnerA")))
        {
            var aName = ownerA.Attr("Name") ?? "";
            foreach (var ownerB in ownerA.ElementsLocal("OwnerB"))
            {
                var bName = ownerB.Attr("Name") ?? "";
                foreach (var link in ownerB.ElementsLocal("Link"))
                {
                    var varA = LinkVar(link, "VarA");
                    var varB = LinkVar(link, "VarB");
                    TryAddLink(result, CombinePath(aName, varA), CombinePath(bName, varB));
                }
            }
        }

        foreach (var link in root.DescendantsAndSelf().Where(e => Local(e, "Link")))
        {
            if (link.Parent is not null && Local(link.Parent, "OwnerB"))
                continue;
            var varA = LinkVar(link, "VarA");
            var varB = LinkVar(link, "VarB");
            TryAddLink(result, varA, varB);
        }

        return result;
    }

    private static string? LinkVar(XElement link, string name) =>
        FirstNonEmpty(link.Attr(name), link.ChildText(name));

    private static string CombinePath(string owner, string? variable)
    {
        var o = owner?.Trim() ?? "";
        var v = variable?.Trim() ?? "";
        if (string.IsNullOrEmpty(v))
            return o;
        if (string.IsNullOrEmpty(o) || HasTreePrefix(v))
            return v;
        return o + "^" + v;
    }

    private static void TryAddLink(List<IoPlcLink> list, string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return;
        var aIo = IsIoPath(a);
        var aPlc = IsPlcPath(a);
        var bIo = IsIoPath(b);
        var bPlc = IsPlcPath(b);
        if (aIo && bPlc)
            list.Add(new IoPlcLink(a, b));
        else if (bIo && aPlc)
            list.Add(new IoPlcLink(b, a));
    }

    internal static bool IsIoPath(string path) =>
        path.StartsWith("TIID^", StringComparison.OrdinalIgnoreCase)
        || path.Contains("^TIID^", StringComparison.OrdinalIgnoreCase)
        || path.Equals("TIID", StringComparison.OrdinalIgnoreCase);

    internal static bool IsPlcPath(string path) =>
        path.StartsWith("TIPC^", StringComparison.OrdinalIgnoreCase)
        || path.Contains("^TIPC^", StringComparison.OrdinalIgnoreCase)
        || path.Equals("TIPC", StringComparison.OrdinalIgnoreCase);

    internal static string PlcVariableDisplayName(string path)
    {
        var segs = SplitPath(path, skipPrefixes: false);
        return segs.Count == 0 ? path.Trim() : segs[^1];
    }

    private static bool AttachLink(List<TopologyNode> roots, string ioPath, string plcName)
    {
        var segments = SplitPath(ioPath, skipPrefixes: true);
        if (segments.Count == 0)
            return false;

        TopologyNode? current = null;
        IReadOnlyList<TopologyNode> level = roots;
        var consumed = 0;
        for (; consumed < segments.Count; consumed++)
        {
            var match = FindInLevel(level, segments[consumed]);
            if (match is null)
                break;
            current = match;
            level = match.Children;
        }

        if (current is null)
            return false;

        var leftover = segments.Skip(consumed).ToList();
        var channel = FormatChannelLabel(leftover);
        if (!current.LinkedSymbols.Contains(plcName, StringComparer.OrdinalIgnoreCase))
            current.LinkedSymbols.Add(plcName);

        if (string.IsNullOrEmpty(channel))
            return true;

        var childName = channel + "    " + plcName;
        if (!current.Children.Any(c => c.Kind == TopologyKind.Variable &&
                                       c.Name.Equals(childName, StringComparison.OrdinalIgnoreCase)))
        {
            var child = new TopologyNode
            {
                Name = childName,
                Kind = TopologyKind.Variable
            };
            child.LinkedSymbols.Add(plcName);
            current.Children.Add(child);
        }
        return true;
    }

    private static string? FormatChannelLabel(List<string> leftover)
    {
        if (leftover.Count == 0)
            return null;
        var parts = leftover.ToList();
        if (parts.Count > 0 && IsPdoEntryName(parts[^1]))
            parts.RemoveAt(parts.Count - 1);
        return parts.Count == 0 ? leftover[0] : string.Join(" ", parts);
    }

    private static bool IsPdoEntryName(string name) =>
        name.Equals("Input", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Output", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Status", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Control", StringComparison.OrdinalIgnoreCase)
        || name.Equals("InfoData", StringComparison.OrdinalIgnoreCase);

    private static TopologyNode? FindInLevel(IReadOnlyList<TopologyNode> nodes, string segment)
    {
        var candidates = nodes.Where(n => n.Kind != TopologyKind.Variable).ToList();
        var exact = candidates.Where(n => n.Name.Equals(segment, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1)
            return exact[0];
        if (exact.Count > 1)
            return null;

        var prefixed = candidates.Where(n =>
            n.Name.StartsWith(segment, StringComparison.OrdinalIgnoreCase) &&
            (n.Name.Length == segment.Length || char.IsWhiteSpace(n.Name[segment.Length]) || n.Name[segment.Length] is '(' or '-')).ToList();
        if (prefixed.Count == 1)
            return prefixed[0];

        // Walk through a synthetic EtherCAT Master (or similar) that is not in the TIID path.
        foreach (var pass in candidates.Where(n => n.Kind == TopologyKind.Master))
        {
            var found = FindInLevel(pass.Children, segment);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static List<string> SplitPath(string path, bool skipPrefixes)
    {
        var segs = path.Split('^', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .ToList();
        if (skipPrefixes)
            segs.RemoveAll(HasTreePrefix);
        return segs;
    }

    private static bool HasTreePrefix(string segment)
    {
        var head = segment.Split('^')[0].Trim();
        return head.Equals("TIID", StringComparison.OrdinalIgnoreCase)
               || head.Equals("TIPC", StringComparison.OrdinalIgnoreCase)
               || head.Equals("TIXC", StringComparison.OrdinalIgnoreCase)
               || head.Equals("TIRS", StringComparison.OrdinalIgnoreCase)
               || head.Equals("TINC", StringComparison.OrdinalIgnoreCase)
               || head.Equals("TISS", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Local(XElement e, string name) =>
        e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase);
}

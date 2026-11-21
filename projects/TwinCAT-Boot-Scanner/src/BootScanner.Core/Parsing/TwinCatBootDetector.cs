using BootScanner.Core.Models;

namespace BootScanner.Core.Parsing;

/// <summary>
/// Conservative TwinCAT 2 vs 3 detector. Never assumes a generation without official path or file evidence.
/// TwinCAT 2 System Manager hardware project is CurrentConfig.tsm (from 2.9) or CurrentConfig.wsm (older),
/// not CurrentConfig.xml — though xml may also exist under a TwinCAT 2 Boot directory.
/// </summary>
public static class TwinCatBootDetector
{
    public static TwinCatBootKind Detect(string bootDirectory)
    {
        var tc3 = new List<string>();
        var tc2 = new List<string>();
        string? hardware = null;
        string? layout = null;

        if (string.IsNullOrWhiteSpace(bootDirectory) || !BootFileSystem.IsDirectory(bootDirectory))
        {
            return Make(TwinCatGeneration.Unknown, "目录不存在，无法分辨 TwinCAT 2 / 3。", null, null, tc3, tc2);
        }

        var boot = Path.GetFullPath(bootDirectory);
        var norm = Normalize(boot);

        if (IsUmRtPath(norm))
        {
            tc3.Add("路径符合 TwinCAT 3 用户模式运行时（Runtimes\\UmRT_）");
            layout = "用户模式运行时布局";
        }
        else if (IsProgramData4026Path(norm))
        {
            tc3.Add("路径符合 TwinCAT 3，3.1.4026+ ProgramData 布局");
            layout = "3.1.4026+ ProgramData 布局";
        }
        else if (IsTwinCat3BootPath(norm))
        {
            tc3.Add("路径含 3.1\\Boot（TwinCAT 3 官方 Boot 位置）");
        }

        if (IsTwinCat2DefaultPath(norm))
            tc2.Add("路径为 TwinCAT\\Boot（非 3.1，TwinCAT 2 官方 Boot 位置）");

        // Official TC3 files
        foreach (var f in EnumerateOfficialTc3PlcFiles(boot))
            tc3.Add(RelName(boot, f));

        var tszip = Path.Combine(boot, "CurrentConfig.tszip");
        if (BootFileSystem.IsFile(tszip))
            tc3.Add("CurrentConfig.tszip");

        foreach (var tmiDir in new[] { "Tmi", "TMI" })
        {
            var dir = Path.Combine(boot, tmiDir);
            if (BootFileSystem.IsDirectory(dir) && SafeFiles(dir, "*.tmi").Count > 0)
            {
                tc3.Add($"Boot\\{tmiDir}\\*.tmi");
                break;
            }
        }

        if (SafeFiles(boot, "*.bootdata").Count > 0 ||
            SafeFiles(boot, "*.bootdata-old").Count > 0 ||
            PlcFolders(boot).Any(d => SafeFiles(d, "*.bootdata").Count > 0 || SafeFiles(d, "*.bootdata-old").Count > 0))
        {
            tc3.Add("*.bootdata / *.bootdata-old");
        }

        // Official TC2 hardware project (fixed name CurrentConfig, suffix .tsm / older .wsm)
        var tsm = Path.Combine(boot, "CurrentConfig.tsm");
        var wsm = Path.Combine(boot, "CurrentConfig.wsm");
        if (BootFileSystem.IsFile(tsm))
        {
            hardware = "CurrentConfig.tsm";
            tc2.Add("Boot 根目录 CurrentConfig.tsm（TwinCAT 2 系统管理器硬件配置）");
        }
        if (BootFileSystem.IsFile(wsm))
        {
            hardware ??= "CurrentConfig.wsm";
            tc2.Add("Boot 根目录 CurrentConfig.wsm（TwinCAT 2 旧版硬件配置）");
        }

        foreach (var f in SafeFiles(boot, "*.tpy"))
        {
            var name = Path.GetFileName(f);
            // CurrentPlc_*.tpy is a common Boot download name in official examples, not a fixed System Manager filename.
            if (LooksLikeRuntimeCurrentPlcTpy(name))
                tc2.Add($"Boot 根目录 {name}（PLC Control 下载到 Boot 时的常见文件名，按运行时编号，不是固定文件名）");
        }

        foreach (var dir in PlcFolders(boot))
        {
            foreach (var f in SafeFiles(dir, "*.tpy"))
                tc3.Add($"Plc\\{Path.GetFileName(f)}（符号描述与 PLC 工程同名）");
        }

        foreach (var pattern in new[] { "TCPLC_P_*.wbp", "TCPLC_S_*.wbp", "TCPLC_R_*.wbp", "TCPLC_T_*.wbp", "TCPLC_R_*.wb~", "TCPLC_T_*.wb~" })
        {
            foreach (var f in SafeFiles(boot, pattern))
                tc2.Add(Path.GetFileName(f));
        }

        var xmlPath = Path.Combine(boot, "CurrentConfig.xml");
        var xmlPresent = BootFileSystem.IsFile(xmlPath);
        if (xmlPresent)
            PeekCurrentConfigXml(xmlPath, tc3, tc2, hardware, IsTwinCat2DefaultPath(norm));

        var generation = Decide(tc3, tc2);
        var reason = BuildReason(generation, tc3, tc2, xmlPresent, hardware, IsTwinCat2DefaultPath(norm));
        // Layout note only when the path actually matches those layouts, and only as a TC3 note.
        if (generation is TwinCatGeneration.TwinCAT2)
            layout = null;
        if (generation is TwinCatGeneration.Unknown)
            layout = null;

        return Make(generation, reason, layout, hardware, tc3, tc2);
    }

    private static TwinCatGeneration Decide(List<string> tc3, List<string> tc2)
    {
        if (tc3.Count > 0 && tc2.Count > 0)
            return TwinCatGeneration.Conflict;
        if (tc3.Count > 0)
            return TwinCatGeneration.TwinCAT3;
        if (tc2.Count > 0)
            return TwinCatGeneration.TwinCAT2;
        return TwinCatGeneration.Unknown;
    }

    private static string BuildReason(
        TwinCatGeneration generation,
        List<string> tc3,
        List<string> tc2,
        bool xmlPresent,
        string? hardware,
        bool tc2Path)
    {
        string Join(IEnumerable<string> items) => string.Join("；", items.Take(6));

        switch (generation)
        {
            case TwinCatGeneration.TwinCAT3:
                return "依据：" + Join(tc3);
            case TwinCatGeneration.TwinCAT2:
            {
                var parts = new List<string> { "依据：" + Join(tc2) };
                parts.Add("TwinCAT 2 系统管理器硬件配置固定文件名为 CurrentConfig.tsm（2.9 起）或旧版 CurrentConfig.wsm，位于 Boot 根目录，不是工程名文件，也不是 CurrentConfig.xml");
                if (xmlPresent)
                    parts.Add("同时存在 CurrentConfig.xml（TwinCAT 2.8 起也可出现在 Boot 根目录，仅作辅助配置描述）");
                if (tc2Path && hardware is null && xmlPresent)
                    parts.Add("未找到 CurrentConfig.tsm / CurrentConfig.wsm，请核对该硬件工程文件");
                return string.Join("。", parts) + "。";
            }
            case TwinCatGeneration.Conflict:
                return "同时发现 TwinCAT 2 与 TwinCAT 3 的版本证据，暂不能确定版本；这不代表目录损坏。"
                       + " TwinCAT 3：" + Join(tc3)
                       + "。 TwinCAT 2：" + Join(tc2) + "。";
            default:
            {
                var why = new List<string>
                {
                    "未发现 Plc\\Port_*.tmc / Plc\\Port_*.app、CurrentConfig.tsm / CurrentConfig.wsm、*.tpy、TCPLC_P_*.wbp 等典型文件，路径也不是官方默认 Boot 位置"
                };
                if (xmlPresent)
                    why.Add("虽有 CurrentConfig.xml，但仅凭该文件名不足以分辨版本（TwinCAT 2 与 3 都可能使用此文件名，且 TwinCAT 2 硬件工程应为 .tsm / .wsm）");
                return string.Join("。", why) + "。";
            }
        }
    }

    private static void PeekCurrentConfigXml(
        string xmlPath,
        List<string> tc3,
        List<string> tc2,
        string? hardware,
        bool tc2Path)
    {
        try
        {
            var doc = XmlUtil.LoadXDocument(xmlPath);
            var root = doc.Root;
            if (root is null)
                return;
            var rootName = root.Name.LocalName;
            var version = root.Attribute("TcVersion")?.Value
                          ?? root.Attributes().FirstOrDefault(a =>
                                 a.Name.LocalName.Equals("TcVersion", StringComparison.OrdinalIgnoreCase))?.Value;

            if (rootName.Equals("TcSmProject", StringComparison.OrdinalIgnoreCase) ||
                (version is not null && version.TrimStart().StartsWith("3", StringComparison.Ordinal)))
            {
                var desc = $"CurrentConfig.xml 根元素 {rootName}";
                if (!string.IsNullOrEmpty(version) && version.TrimStart().StartsWith("3", StringComparison.Ordinal))
                    desc += $"，TcVersion {version}";
                tc3.Add(desc);
                return;
            }

            if (rootName.Equals("TcConfig", StringComparison.OrdinalIgnoreCase) ||
                (rootName.Equals("TreeItem", StringComparison.OrdinalIgnoreCase)))
            {
                // XML format is a TC2-era signal, but this file is not the System Manager hardware project.
                var hw = hardware ?? "CurrentConfig.tsm / CurrentConfig.wsm";
                tc2.Add($"CurrentConfig.xml 根元素 {rootName}（辅助配置描述，不是系统管理器硬件工程；硬件工程应为 {hw}）");
            }
            else if (tc2Path)
            {
                // Do not treat xml as the hardware project; just note it under a TC2 path.
                // Path already counted as TC2 evidence. Mention xml without adding a second strong "this is the hardware file" claim.
                tc2.Add("默认 TwinCAT 2 路径下存在 CurrentConfig.xml（辅助文件；请同时核对其是否有 CurrentConfig.tsm / CurrentConfig.wsm）");
            }
        }
        catch
        {
            // Unreadable XML is not evidence for either side; the scan will report the parse failure separately.
        }
    }

    internal static string Normalize(string path)
    {
        var s = path.Replace('\\', '/');
        while (s.Contains("//", StringComparison.Ordinal))
            s = s.Replace("//", "/", StringComparison.Ordinal);
        return s.TrimEnd('/');
    }

    internal static bool IsTwinCat3BootPath(string normalized) =>
        HasPathSegment(normalized, "/3.1/Boot")
        || HasPathSegment(normalized, "/ProgramData/Beckhoff/TwinCAT/3.1")
        || IsUmRtPath(normalized);

    internal static bool IsProgramData4026Path(string normalized) =>
        HasPathSegment(normalized, "/ProgramData/Beckhoff/TwinCAT/3.1")
        && !IsUmRtPath(normalized);

    internal static bool IsUmRtPath(string normalized) =>
        normalized.IndexOf("Runtimes/UmRT_", StringComparison.OrdinalIgnoreCase) >= 0
        || (normalized.IndexOf("Runtimes/", StringComparison.OrdinalIgnoreCase) >= 0
            && normalized.IndexOf("UmRT", StringComparison.OrdinalIgnoreCase) >= 0
            && HasPathSegment(normalized, "/3.1/Boot"));

    private static bool HasPathSegment(string path, string suffix) =>
        path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
        || path.Contains(suffix + "/", StringComparison.OrdinalIgnoreCase);

    internal static bool IsTwinCat2DefaultPath(string normalized)
    {
        if (IsTwinCat3BootPath(normalized))
            return false;
        return normalized.EndsWith("/TwinCAT/Boot", StringComparison.OrdinalIgnoreCase)
               || normalized.IndexOf("/Hard Disk/TwinCAT/Boot", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.EndsWith("/Hard Disk/TwinCAT/Boot", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> EnumerateOfficialTc3PlcFiles(string boot)
    {
        var list = new List<string>();
        foreach (var dir in PlcFolders(boot))
        {
            list.AddRange(SafeFiles(dir, "Port_*.tmc"));
            list.AddRange(SafeFiles(dir, "Port_*.app"));
            // Official table also allows PLC_Name.tmc (typically Port_851.tmc). Other *.tmc under Plc\ count.
            foreach (var tmc in SafeFiles(dir, "*.tmc"))
            {
                if (!list.Contains(tmc, StringComparer.OrdinalIgnoreCase))
                    list.Add(tmc);
            }
        }
        return list;
    }

    internal static IEnumerable<string> PlcFolders(string boot)
    {
        foreach (var name in new[] { "Plc", "PLC", "plc", "TcPlc" })
        {
            var dir = Path.Combine(boot, name);
            if (BootFileSystem.IsDirectory(dir))
                yield return dir;
        }
    }

    internal static List<string> SafeFiles(string directory, string pattern)
    {
        try
        {
            if (!BootFileSystem.IsDirectory(directory))
                return new List<string>();
            return BootFileSystem.Files(directory, pattern)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Official docs use CurrentPlc_1.tpy … CurrentPlc_4.tpy as examples of names after
    /// "Download symbol description" to a TwinCAT 2 Boot directory. This is not a fixed
    /// hardware-project name like CurrentConfig.tsm.
    /// </summary>
    internal static bool LooksLikeRuntimeCurrentPlcTpy(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;
        if (!fileName.EndsWith(".tpy", StringComparison.OrdinalIgnoreCase))
            return false;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.Equals("CurrentPlc", StringComparison.OrdinalIgnoreCase))
            return true;
        const string prefix = "CurrentPlc_";
        if (stem.Length <= prefix.Length ||
            !stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var rest = stem[prefix.Length..];
        return rest.Length > 0 && rest.All(char.IsDigit);
    }

    private static string RelName(string boot, string full)
    {
        try
        {
            return Path.GetRelativePath(boot, full).Replace('/', '\\');
        }
        catch
        {
            return Path.GetFileName(full);
        }
    }

    private static TwinCatBootKind Make(
        TwinCatGeneration generation,
        string reason,
        string? layout,
        string? hardware,
        List<string> tc3,
        List<string> tc2) => new()
    {
        Generation = generation,
        Reason = reason,
        LayoutNote = layout,
        HardwareConfigFile = hardware,
        TwinCat3Evidence = tc3,
        TwinCat2Evidence = tc2
    };
}

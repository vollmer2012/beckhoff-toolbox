using BootScanner.Core;
using BootScanner.Core.Models;

namespace BootScanner.Core.Parsing;

/// <summary>
/// Orchestrates a read-only scan of a TwinCAT Boot directory.
/// Never writes into the Boot folder.
/// </summary>
public sealed class BootScanService
{
    private static readonly string[] PlcFolderNames = { "Plc", "PLC", "plc", "TcPlc" };
    private static readonly string[] TmiFolderNames = { "Tmi", "TMI" };

    private readonly CurrentConfigParser _configParser = new();
    private readonly TmcTmiParser _tmcParser = new();
    private readonly TpyParser _tpyParser = new();

    public BootScanResult Scan(string inputPath) => Scan(inputPath, CancellationToken.None);

    public BootScanResult Scan(string inputPath, CancellationToken cancellationToken)
        => Scan(inputPath, cancellationToken, null);

    public BootScanResult Scan(string inputPath, CancellationToken cancellationToken, IProgress<string>? parsedFileProgress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = new List<ParseStatusEntry>();
        var topology = new List<TopologyNode>();
        var symbols = new List<SymbolEntry>();

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            status.Add(Failed("路径", "未指定目录"));
            return Result("", inputPath, topology, symbols, null, status);
        }

        var raw = inputPath.Trim().Trim('"');
        if (!BootFileSystem.IsFile(raw) && !BootFileSystem.IsDirectory(raw))
        {
            status.Add(Failed("路径", "路径不存在"));
            return Result(raw, raw, topology, symbols, null, status);
        }

        var boot = BootFolderLocator.Resolve(raw);
        if (boot is null)
        {
            status.Add(Failed("路径",
                "未找到 Boot 目录（需要 CurrentConfig.xml / CurrentConfig.tsm / CurrentConfig.wsm，或 Plc\\Port_*.tmc、*.tpy、TCPLC_P_*.wbp）"));
            return Result(
                BootFileSystem.IsDirectory(raw) ? Path.GetFullPath(raw) : raw,
                raw, topology, symbols, null, status);
        }

        var kind = TwinCatBootDetector.Detect(boot);
        cancellationToken.ThrowIfCancellationRequested();
        status.Add(new ParseStatusEntry
        {
            FileName = AppInfo.TitleWithVersion + "  ·  " + kind.StatusHeadline,
            Outcome = kind.Generation switch
            {
                TwinCatGeneration.Unknown => ParseOutcome.Failed,
                TwinCatGeneration.Conflict => ParseOutcome.Recognized,
                _ => ParseOutcome.Recognized
            },
            Detail = kind.Reason,
            IsHeadline = true
        });

        ScanHardwareAndConfig(boot, kind, topology, symbols, status);
        cancellationToken.ThrowIfCancellationRequested();
        ScanSymbolFiles(boot, kind, symbols, status, cancellationToken, parsedFileProgress);
        cancellationToken.ThrowIfCancellationRequested();
        AddUnlistedFiles(boot, status, cancellationToken);
        SortStatus(status);
        cancellationToken.ThrowIfCancellationRequested();

        return Result(boot, raw, topology, Dedup(symbols), kind, status);
    }

    private void ScanHardwareAndConfig(
        string boot,
        TwinCatBootKind kind,
        List<TopologyNode> topology,
        List<SymbolEntry> symbols,
        List<ParseStatusEntry> status)
    {
        var tsm = Path.Combine(boot, "CurrentConfig.tsm");
        var wsm = Path.Combine(boot, "CurrentConfig.wsm");
        var xml = Path.Combine(boot, "CurrentConfig.xml");
        var tszip = Path.Combine(boot, "CurrentConfig.tszip");

        if (BootFileSystem.IsFile(tsm))
            ScanTsm(boot, tsm, topology, symbols, status);
        if (BootFileSystem.IsFile(wsm))
            ScanTsm(boot, wsm, topology, symbols, status);

        if (kind.Generation == TwinCatGeneration.TwinCAT2 && !BootFileSystem.IsFile(tsm) && !BootFileSystem.IsFile(wsm))
        {
            status.Add(new ParseStatusEntry
            {
                FileName = "CurrentConfig.tsm / CurrentConfig.wsm",
                Outcome = ParseOutcome.Failed,
                Detail = "未找到 TwinCAT 2 系统管理器硬件配置（固定文件名 CurrentConfig.tsm，旧版为 CurrentConfig.wsm）"
            });
        }

        if (BootFileSystem.IsFile(xml))
        {
            var parsed = _configParser.ParseFile(xml);
            var detail = parsed.Success ? parsed.Summary : (parsed.Error ?? parsed.Summary);
            if (parsed.Success && (BootFileSystem.IsFile(tsm) || BootFileSystem.IsFile(wsm) || kind.Generation == TwinCatGeneration.TwinCAT2))
            {
                var hw = kind.HardwareConfigFile ?? "CurrentConfig.tsm / CurrentConfig.wsm";
                detail += $"（辅助配置描述，不是系统管理器硬件工程；硬件工程为 {hw}）";
            }

            status.Add(new ParseStatusEntry
            {
                FileName = "CurrentConfig.xml",
                RelativePath = Rel(boot, xml),
                Outcome = parsed.Success ? ParseOutcome.Parsed : ParseOutcome.Failed,
                Detail = detail
            });
            if (parsed.Success)
            {
                var auxiliary = BootFileSystem.IsFile(tsm) || BootFileSystem.IsFile(wsm) || kind.Generation == TwinCatGeneration.TwinCAT2;
                if (auxiliary && parsed.SystemConfig is not null)
                    CurrentConfigParser.MarkSystemAsAuxiliary(parsed.SystemConfig);
                topology.AddRange(parsed.Roots);
                if (auxiliary && parsed.SystemConfig is null)
                {
                    status.Add(new ParseStatusEntry
                    {
                        FileName = "系统配置",
                        Outcome = ParseOutcome.Recognized,
                        Detail = "已识别，系统参数暂不可用"
                    });
                }
                if (auxiliary && parsed.MappingLinkCount == 0)
                {
                    status.Add(new ParseStatusEntry
                    {
                        FileName = "I/O 关联",
                        Outcome = ParseOutcome.Recognized,
                        Detail = "I/O 与 PLC 变量关联暂不可用"
                    });
                }
            }
        }
        else
        {
            // TC3 (and unknown/conflict without xml): missing xml is a visible failure.
            // On a confirmed TC2 boot the hardware project is .tsm/.wsm; xml is optional.
            if (kind.Generation != TwinCatGeneration.TwinCAT2)
            {
                status.Add(new ParseStatusEntry
                {
                    FileName = "CurrentConfig.xml",
                    RelativePath = "CurrentConfig.xml",
                    Outcome = ParseOutcome.Failed,
                    Detail = "未找到该文件"
                });
            }

            if (BootFileSystem.IsFile(tsm) || BootFileSystem.IsFile(wsm) || kind.Generation == TwinCatGeneration.TwinCAT2)
            {
                // No usable System/Mappings XML: System Manager streams are not parsed.
                status.Add(new ParseStatusEntry
                {
                    FileName = "系统配置",
                    Outcome = ParseOutcome.Recognized,
                    Detail = "已识别，系统参数暂不可用"
                });
                status.Add(new ParseStatusEntry
                {
                    FileName = "I/O 关联",
                    Outcome = ParseOutcome.Recognized,
                    Detail = "I/O 与 PLC 变量关联暂不可用"
                });
            }
        }

        if (BootFileSystem.IsFile(tszip))
        {
            status.Add(new ParseStatusEntry
            {
                FileName = "CurrentConfig.tszip",
                RelativePath = Rel(boot, tszip),
                Outcome = ParseOutcome.Skipped,
                Detail = "配置压缩包（tsproj/xti），第一版不解析"
            });
        }
    }

    private static void ScanTsm(string boot, string path, List<TopologyNode> topology, List<SymbolEntry> symbols, List<ParseStatusEntry> status)
    {
        var parsed = TsmProjectParser.Parse(path);
        topology.AddRange(parsed.Roots);
        symbols.AddRange(parsed.Symbols);
        status.Add(new ParseStatusEntry
        {
            FileName = Path.GetFileName(path),
            RelativePath = Rel(boot, path),
            Outcome = parsed.Outcome,
            Detail = parsed.Detail
        });
    }

    private void ScanSymbolFiles(
        string boot,
        TwinCatBootKind kind,
        List<SymbolEntry> symbols,
        List<ParseStatusEntry> status,
        CancellationToken cancellationToken,
        IProgress<string>? parsedFileProgress)
    {
        var tmcFiles = ListTmcTmiFiles(boot, cancellationToken);
        var tpyFiles = ListTpyFiles(boot, cancellationToken);

        if (tpyFiles.Count == 0 &&
            kind.Generation is TwinCatGeneration.TwinCAT2 or TwinCatGeneration.Conflict &&
            kind.TwinCat2Evidence.Count > 0)
        {
            status.Add(new ParseStatusEntry
            {
                FileName = "*.tpy",
                Outcome = ParseOutcome.Unavailable,
                Detail = "未找到符号描述文件（*.tpy）。工程目录中的 tpy 与 PLC 工程同名；只有在 PLC Control 勾选「Download symbol description」时才会下载到目标机 Boot。Boot 根目录常见例如 CurrentPlc_1.tpy（对应第 1 个运行时），但不是固定文件名。"
            });
        }

        if (tmcFiles.Count == 0 && tpyFiles.Count == 0 && kind.Generation != TwinCatGeneration.TwinCAT2)
        {
            var detail = kind.Generation == TwinCatGeneration.TwinCAT3
                ? "未找到 Plc\\Port_*.tmc / *.tmi"
                : "未找到符号表文件（TwinCAT 3：Plc\\Port_*.tmc / *.tmi 或 Plc\\*.tpy；TwinCAT 2：Boot 根目录 *.tpy）";
            status.Add(new ParseStatusEntry
            {
                FileName = "符号表",
                Outcome = ParseOutcome.Unavailable,
                Detail = detail
            });
        }

        foreach (var file in tpyFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = _tpyParser.ParseFile(file);
            status.Add(new ParseStatusEntry
            {
                FileName = Path.GetFileName(file),
                RelativePath = Rel(boot, file),
                Outcome = parsed.Success ? ParseOutcome.Parsed : ParseOutcome.Failed,
                Detail = parsed.Success ? parsed.Summary : (parsed.Error ?? parsed.Summary)
            });
            if (parsed.Success)
                symbols.AddRange(parsed.Symbols);
            parsedFileProgress?.Report(Rel(boot, file));
        }

        foreach (var file in tmcFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = _tmcParser.ParseFile(file);
            status.Add(new ParseStatusEntry
            {
                FileName = Path.GetFileName(file),
                RelativePath = Rel(boot, file),
                Outcome = parsed.Success ? ParseOutcome.Parsed : ParseOutcome.Failed,
                Detail = parsed.Success ? parsed.Summary : (parsed.Error ?? parsed.Summary)
            });
            if (parsed.Success)
                symbols.AddRange(parsed.Symbols);
            parsedFileProgress?.Report(Rel(boot, file));
        }
    }

    private static void NoteSkippedBinaries(string boot, TwinCatBootKind kind, List<ParseStatusEntry> status)
    {
        var listed = new HashSet<string>(
            status.Where(s => !string.IsNullOrEmpty(s.RelativePath)).Select(s => NormalizeRel(s.RelativePath!)),
            StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateBootFiles(boot))
        {
            if (!TryOfficialSkipDetail(path, out var detail))
                continue;
            var rel = Rel(boot, path);
            if (!listed.Add(NormalizeRel(rel)))
                continue;
            status.Add(new ParseStatusEntry
            {
                FileName = Path.GetFileName(path),
                RelativePath = rel,
                Outcome = ParseOutcome.Skipped,
                Detail = detail
            });
        }
    }

    private static void AddUnlistedFiles(string boot, List<ParseStatusEntry> status, CancellationToken cancellationToken)
    {
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in status)
        {
            if (!string.IsNullOrEmpty(s.RelativePath))
                listed.Add(NormalizeRel(s.RelativePath));
        }

        foreach (var path in EnumerateBootFiles(boot, cancellationToken))
        {
            var rel = Rel(boot, path);
            if (!listed.Add(NormalizeRel(rel)))
                continue;

            ParseOutcome outcome;
            string detail;
            if (TryOfficialSkipDetail(path, out detail)) outcome = ParseOutcome.Skipped;
            else ClassifyUnparsedFile(Path.GetFileName(path), out outcome, out detail);
            status.Add(new ParseStatusEntry
            {
                FileName = Path.GetFileName(path),
                RelativePath = rel,
                Outcome = outcome,
                Detail = detail
            });
        }
    }

    private static void SortStatus(List<ParseStatusEntry> status)
    {
        var ordered = status
            .Select((s, i) => (s, i))
            .OrderBy(x => x.s.IsHeadline ? 0 : 1)
            .ThenBy(x => NormalizeRel(x.s.RelativePath ?? x.s.FileName), StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.i)
            .Select(x => x.s)
            .ToList();
        status.Clear();
        status.AddRange(ordered);
    }

    private static string NormalizeRel(string relative) =>
        relative.Replace('\\', '/').TrimStart('/');

    private static IEnumerable<string> EnumerateBootFiles(string boot, CancellationToken cancellationToken = default)
    {
        IEnumerable<string> files;
        try
        {
            files = BootFileSystem.Files(boot, recursive: true, cancellationToken: cancellationToken);
        }
        catch
        {
            yield break;
        }

        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string rel;
            try { rel = Path.GetRelativePath(boot, path); }
            catch { continue; }
            // Stay inside the Boot folder; do not follow a junction out of the tree.
            if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
                continue;
            yield return path;
        }
    }

    internal static bool TryOfficialSkipDetail(string path, out string detail)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(name);

        if (ext.Equals(".app", StringComparison.OrdinalIgnoreCase))
        {
            detail = "二进制启动项目，不反编译";
            return true;
        }
        if (ext.Equals(".bootdata", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bootdata-old", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".bootdata-old", StringComparison.OrdinalIgnoreCase))
        {
            detail = "持久化数据，不解析";
            return true;
        }
        if (ext.Equals(".tszip", StringComparison.OrdinalIgnoreCase))
        {
            detail = "配置压缩包（tsproj/xti），第一版不解析";
            return true;
        }
        if (ext.Equals(".wbp", StringComparison.OrdinalIgnoreCase) || ext.Equals(".wb~", StringComparison.OrdinalIgnoreCase))
        {
            detail = ClassifyWbp(name);
            return true;
        }
        if (ext.Equals(".tizip", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tpzip", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".autostart", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".cid", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".crc", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".occ", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".oce", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ocm", StringComparison.OrdinalIgnoreCase))
        {
            detail = "官方引导附加文件，不解析";
            return true;
        }

        detail = "";
        return false;
    }

    private static string ClassifyWbp(string name)
    {
        if (name.StartsWith("TCPLC_P_", StringComparison.OrdinalIgnoreCase))
            return "TwinCAT 2 启动项目，不反编译";
        if (name.StartsWith("TCPLC_S_", StringComparison.OrdinalIgnoreCase))
            return "打包源码，不解析";
        if (name.StartsWith("TCPLC_R_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("TCPLC_T_", StringComparison.OrdinalIgnoreCase))
            return "RETAIN/PERSISTENT 数据，不解析";
        return "TwinCAT 2 启动/保持数据，不反编译";
    }

    internal static void ClassifyUnparsedFile(string fileName, out ParseOutcome outcome, out string detail)
    {
        var ext = Path.GetExtension(fileName);
        if (IsKnownParseableExtension(ext) || IsKnownHardwareName(fileName, ext))
        {
            outcome = ParseOutcome.Recognized;
            detail = "已知 Boot 文件扩展名，但不在本版解析的位置，未读取内容";
            return;
        }

        outcome = ParseOutcome.Unknown;
        if (string.IsNullOrEmpty(ext))
            detail = "无扩展名，未列入已知 Boot 文件类型，未读取内容";
        else
            detail = "扩展名 " + ext + "，未列入已知 Boot 文件类型，未读取内容";
    }

    private static bool IsKnownParseableExtension(string ext) =>
        ext.Equals(".xml", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".tmc", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".tmi", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".tpy", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownHardwareName(string fileName, string ext) =>
        ext.Equals(".tsm", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".wsm", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("CurrentConfig.tsm", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("CurrentConfig.wsm", StringComparison.OrdinalIgnoreCase);

    internal static List<string> ListTmcTmiFiles(string boot, CancellationToken cancellationToken = default)
    {
        var list = new List<string>();
        void AddFrom(string dir, string pattern)
        {
            if (!BootFileSystem.IsDirectory(dir))
                return;
            try
            {
                list.AddRange(BootFileSystem.Files(dir, pattern, cancellationToken: cancellationToken));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* unavailable directory */ }
        }

        AddFrom(boot, "*.tmc");
        AddFrom(boot, "*.tmi");
        foreach (var name in PlcFolderNames)
        {
            AddFrom(Path.Combine(boot, name), "*.tmc");
            AddFrom(Path.Combine(boot, name), "*.tmi");
        }
        foreach (var name in TmiFolderNames)
            AddFrom(Path.Combine(boot, name), "*.tmi");

        return DistinctPaths(list);
    }

    internal static List<string> ListTpyFiles(string boot, CancellationToken cancellationToken = default)
    {
        var list = new List<string>();
        // Official name is PLC_Name.tpy (same as the PLC project). CurrentPlc_*.tpy is only a common Boot example.
        list.AddRange(BootFileSystem.Files(boot, "*.tpy", cancellationToken: cancellationToken));
        foreach (var name in PlcFolderNames)
        {
            var dir = Path.Combine(boot, name);
            if (BootFileSystem.IsDirectory(dir))
            {
                list.AddRange(BootFileSystem.Files(dir, "*.tpy", cancellationToken: cancellationToken));
            }
        }
        return DistinctPaths(list);
    }

    private static List<string> ListInBootAndPlc(string boot, string pattern)
    {
        var list = new List<string>();
        list.AddRange(SafeRoot(boot, pattern));
        foreach (var name in PlcFolderNames)
        {
            var dir = Path.Combine(boot, name);
            if (!BootFileSystem.IsDirectory(dir))
                continue;
            try { list.AddRange(BootFileSystem.Files(dir, pattern)); }
            catch { /* ignore */ }
        }
        return DistinctPaths(list);
    }

    private static List<string> SafeRoot(string boot, string pattern)
    {
        try
        {
            return BootFileSystem.Files(boot, pattern).ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static List<string> DistinctPaths(IEnumerable<string> paths) =>
        paths.Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<SymbolEntry> Dedup(List<SymbolEntry> symbols)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SymbolEntry>();
        foreach (var s in symbols)
        {
            if (seen.Add(s.DedupKey))
                result.Add(s);
        }
        return result;
    }

    private static ParseStatusEntry Failed(string fileName, string detail) => new()
    {
        FileName = fileName,
        Outcome = ParseOutcome.Failed,
        Detail = detail
    };

    private static string Rel(string boot, string full)
    {
        try
        {
            var rel = Path.GetRelativePath(boot, full);
            return string.IsNullOrEmpty(rel) ? Path.GetFileName(full) : rel;
        }
        catch
        {
            return Path.GetFileName(full);
        }
    }

    private static BootScanResult Result(
        string boot,
        string? requested,
        List<TopologyNode> topology,
        List<SymbolEntry> symbols,
        TwinCatBootKind? kind,
        List<ParseStatusEntry> status) => new()
    {
        BootDirectory = boot,
        RequestedPath = requested,
        Topology = topology,
        Symbols = symbols,
        Kind = kind,
        Status = status
    };
}

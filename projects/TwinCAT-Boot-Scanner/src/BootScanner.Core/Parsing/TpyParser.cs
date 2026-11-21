using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using BootScanner.Core.Models;

namespace BootScanner.Core.Parsing;

public sealed class TpyParseResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string RootElement { get; init; } = "";
    public int? AdsPort { get; init; }
    public IReadOnlyList<SymbolEntry> Symbols { get; init; } = Array.Empty<SymbolEntry>();
    public string Summary { get; init; } = "";
}

/// <summary>
/// Read-only parser for TPY symbol files. The official name is PLC_Name.tpy (same as the PLC project);
/// CurrentPlc_*.tpy is only a common example when the file is downloaded into a TwinCAT 2 Boot directory.
/// Typical root is PlcProjectInfo with Symbols/Symbol (Name, Type, IGroup, IOffset, BitSize).
/// </summary>
public sealed class TpyParser
{
    // Official / well-known ADS index groups for PLC process image and memory.
    public const uint IndexGroupInput = 0xF020;
    public const uint IndexGroupInputBit = 0xF021;
    public const uint IndexGroupOutput = 0xF030;
    public const uint IndexGroupOutputBit = 0xF031;
    public const uint IndexGroupMemory = 0x4020;

    public TpyParseResult ParseFile(string path)
    {
        var fileName = Path.GetFileName(path);
        try
        {
            var doc = XmlUtil.LoadXDocument(path);
            return ParseDocument(doc, fileName);
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

    public TpyParseResult ParseDocument(XDocument doc, string? sourceFile = null)
    {
        var root = doc.Root;
        if (root is null)
            return Fail("XML 根元素为空");

        var rootName = root.Name.LocalName;
        var adsPort = TryReadAdsPort(root);
        var symbols = new List<SymbolEntry>();

        foreach (var sym in FindSymbolElements(root))
        {
            var entry = ParseSymbol(sym, sourceFile);
            if (entry is not null)
                symbols.Add(entry);
        }

        if (symbols.Count == 0
            && !rootName.Equals("PlcProjectInfo", StringComparison.OrdinalIgnoreCase)
            && !root.DescendantsLocal("Symbols").Any())
        {
            return Fail($"未识别的 TPY 根元素：{rootName}");
        }

        var summary = symbols.Count > 0
            ? (adsPort is null ? $"{symbols.Count} 个变量" : $"{symbols.Count} 个变量（ADS 端口 {adsPort}）")
            : "0 个变量";

        return new TpyParseResult
        {
            Success = true,
            RootElement = rootName,
            AdsPort = adsPort,
            Symbols = symbols,
            Summary = summary
        };
    }

    private static TpyParseResult Fail(string error) => new()
    {
        Success = false,
        Error = error,
        Summary = error,
        Symbols = Array.Empty<SymbolEntry>()
    };

    private static IEnumerable<XElement> FindSymbolElements(XElement root)
    {
        foreach (var group in root.DescendantsLocal("Symbols"))
        {
            foreach (var sym in group.ElementsLocal("Symbol"))
                yield return sym;
        }

        // Some wrappers nest a single Symbols list only; do not also pick DataType/SubItem as symbols.
    }

    private static int? TryReadAdsPort(XElement root)
    {
        var portEl = root.DescendantsLocal("RoutingInfo")
            .SelectMany(r => r.DescendantsLocal("AdsInfo"))
            .Select(a => a.ElementLocal("Port"))
            .FirstOrDefault(p => p is not null);
        portEl ??= root.DescendantsLocal("AdsInfo").Select(a => a.ElementLocal("Port")).FirstOrDefault(p => p is not null);
        if (portEl is null)
            return null;
        return int.TryParse(portEl.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
            ? port
            : null;
    }

    private static SymbolEntry? ParseSymbol(XElement sym, string? sourceFile)
    {
        var name = sym.ChildText("Name");
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var typeName = sym.ChildText("Type") ?? "UNKNOWN";
        uint? iGroup = TryParseIndex(sym.ChildText("IGroup") ?? sym.ChildText("IndexGroup"));
        uint? iOffset = TryParseIndex(sym.ChildText("IOffset") ?? sym.ChildText("IndexOffset"));
        int? bitSize = null;
        if (int.TryParse(sym.ChildText("BitSize"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bs))
            bitSize = bs;

        var arrayInfo = sym.ElementLocal("ArrayInfo");
        if (arrayInfo is not null
            && int.TryParse(arrayInfo.ChildText("LBound"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lo)
            && int.TryParse(arrayInfo.ChildText("Elements"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
        {
            typeName = $"ARRAY [{lo}..{lo + count - 1}] OF {typeName}";
        }

        var direction = MapIndexGroup(iGroup);
        var iec = TryFormatIec(iGroup, iOffset, bitSize);
        var address = iec ?? FormatIndexAddress(iGroup, iOffset);

        return new SymbolEntry
        {
            Name = name,
            TypeName = typeName,
            Direction = direction,
            AddressDisplay = address,
            IecAddress = iec,
            BitOffset = iOffset is null ? null : (int?)(iOffset.Value * 8),
            SourceFile = sourceFile,
            DataArea = iGroup is null ? null : $"IGroup 0x{iGroup.Value:X4}",
            IndexGroup = iGroup,
            IndexOffset = iOffset
        };
    }

    /// <summary>
    /// Map well-known ADS IGroup values only. Unknown groups are not faked as 内部.
    /// </summary>
    public static SymbolDirection MapIndexGroup(uint? iGroup)
    {
        if (iGroup is null)
            return SymbolDirection.Unknown;
        return iGroup.Value switch
        {
            IndexGroupInput or IndexGroupInputBit => SymbolDirection.Input,
            IndexGroupOutput or IndexGroupOutputBit => SymbolDirection.Output,
            IndexGroupMemory => SymbolDirection.Internal,
            _ => SymbolDirection.Unknown
        };
    }

    /// <summary>
    /// Derive a compact IEC address when IGroup/IOffset meaning is unambiguous; otherwise null.
    /// 0xF020/0xF030/0x4020 treat IOffset as a byte offset; 0xF021/0xF031 treat it as a bit offset.
    /// </summary>
    public static string? TryFormatIec(uint? iGroup, uint? iOffset, int? bitSize)
    {
        if (iGroup is null || iOffset is null)
            return null;

        char? area = iGroup.Value switch
        {
            IndexGroupInput or IndexGroupInputBit => 'I',
            IndexGroupOutput or IndexGroupOutputBit => 'Q',
            IndexGroupMemory => 'M',
            _ => null
        };
        if (area is null)
            return null;

        var bitAccess = iGroup.Value is IndexGroupInputBit or IndexGroupOutputBit;
        uint byteOff;
        uint bit;
        if (bitAccess)
        {
            byteOff = iOffset.Value / 8;
            bit = iOffset.Value % 8;
        }
        else
        {
            byteOff = iOffset.Value;
            bit = 0;
        }

        if (bitAccess || bitSize is 1 || (bitSize is 8 && bit == 0 && (area == 'I' || area == 'Q')))
        {
            // BOOL / bit in the process image: I0.0 style, matching common TwinCAT display.
            if (bitSize is 1 or 8 or null)
                return $"{area}{byteOff}.{bit}";
        }

        if (bitSize is 8)
            return $"{area}B{byteOff}";
        if (bitSize is 16)
            return $"{area}W{byteOff}";
        if (bitSize is 32)
            return $"{area}D{byteOff}";

        // Known area but unusual size: still show a byte address rather than invent a size prefix.
        return bitAccess ? $"{area}{byteOff}.{bit}" : $"{area}B{byteOff}";
    }

    public static string FormatIndexAddress(uint? iGroup, uint? iOffset)
    {
        if (iGroup is null && iOffset is null)
            return "";
        var g = iGroup is null ? "?" : $"0x{iGroup.Value:X4}";
        var o = iOffset is null ? "?" : $"0x{iOffset.Value:X4}";
        return $"{g}:{o}";
    }

    public static uint? TryParseIndex(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var t = raw.Trim();
        if (t.StartsWith("#x", StringComparison.OrdinalIgnoreCase))
            t = "0x" + t[2..];
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
                ? hex
                : null;
        }
        return uint.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec)
            ? dec
            : null;
    }

    private static string Sanitize(string message)
    {
        var first = message.Split('\n')[0].Trim();
        return first.Length > 240 ? first[..240] + "…" : first;
    }
}

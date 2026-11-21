using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using BootScanner.Core.Models;

namespace BootScanner.Core.Parsing;

public sealed class TmcParseResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string RootElement { get; init; } = "";
    public IReadOnlyList<SymbolEntry> Symbols { get; init; } = Array.Empty<SymbolEntry>();
    public string Summary { get; init; } = "";
}

/// <summary>
/// Read-only parser for TwinCAT Module Class (.tmc) and Module Instance (.tmi) XML symbol tables.
/// </summary>
public sealed class TmcTmiParser
{
    private static readonly Regex IecPattern = new(
        @"^%?(I|Q|M|D)(X|B|W|D|L)?[0-9]+(\.[0-7])?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public TmcParseResult ParseFile(string path)
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

    public TmcParseResult ParseDocument(XDocument doc, string? sourceFile = null)
    {
        var root = doc.Root;
        if (root is null)
            return Fail("XML 根元素为空");

        var rootName = root.Name.LocalName;
        var symbols = new List<SymbolEntry>();

        foreach (var area in root.DescendantsLocal("DataArea"))
        {
            var areaNo = area.ElementLocal("AreaNo");
            var areaType = FirstNonEmpty(
                areaNo?.Attr("AreaType"),
                area.Attr("AreaType"),
                area.ChildText("AreaType"),
                area.ChildText("Name")) ?? "";
            var areaName = area.ChildText("Name") ?? areaType;
            var direction = MapDirection(areaType, areaName);

            foreach (var sym in area.ElementsLocal("Symbol"))
            {
                var entry = ParseSymbol(sym, direction, areaName, sourceFile);
                if (entry is not null)
                    symbols.Add(entry);
            }
        }

        if (symbols.Count == 0 &&
            !rootName.Equals("TcModuleClass", StringComparison.OrdinalIgnoreCase) &&
            !rootName.Equals("TcModuleInstance", StringComparison.OrdinalIgnoreCase))
        {
            // Still accept unknown roots if they actually contained DataArea symbols (handled above).
            if (!root.DescendantsLocal("DataTypes").Any() && !root.DescendantsLocal("Modules").Any())
            {
                return Fail($"未识别的 TMC/TMI 根元素：{rootName}");
            }
        }

        string summary;
        if (symbols.Count > 0)
            summary = $"{symbols.Count} 个变量";
        else if (root.DescendantsLocal("DataTypes").Any() || root.DescendantsLocal("Module").Any())
            summary = "0 个变量（仅含类型或实例描述，无 DataArea 符号）";
        else
            summary = "0 个变量";

        return new TmcParseResult
        {
            Success = true,
            RootElement = rootName,
            Symbols = symbols,
            Summary = summary
        };
    }

    private static TmcParseResult Fail(string error) => new()
    {
        Success = false,
        Error = error,
        Summary = error,
        Symbols = Array.Empty<SymbolEntry>()
    };

    internal static SymbolDirection MapDirection(string areaType, string? areaName = null)
    {
        var token = (areaType ?? "").Trim();
        if (token.Length == 0)
            token = areaName ?? "";

        var t = token.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        if (t is "input" or "inputs" or "inputdst" or "inputsrc" or "in")
            return SymbolDirection.Input;
        if (t is "output" or "outputs" or "outputsrc" or "outputdst" or "out")
            return SymbolDirection.Output;
        if (t is "inout" or "in-out" or "in/out")
            return SymbolDirection.InOut;
        if (t is "internal" or "memory" or "standard" or "retain" or "persistent")
            return SymbolDirection.Internal;

        // Fall back on keywords inside names like "PlcTask Inputs".
        var n = (areaName ?? token).ToLowerInvariant();
        if (n.Contains("inout") || n.Contains("in-out"))
            return SymbolDirection.InOut;
        if (n.Contains("input"))
            return SymbolDirection.Input;
        if (n.Contains("output"))
            return SymbolDirection.Output;
        return SymbolDirection.Internal;
    }

    private static SymbolEntry? ParseSymbol(XElement sym, SymbolDirection direction, string? areaName, string? sourceFile)
    {
        var name = sym.ChildText("Name");
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var typeName = FirstNonEmpty(
            sym.ChildText("BaseType"),
            sym.ChildText("Type"),
            "UNKNOWN")!;

        var arrayInfo = sym.ElementLocal("ArrayInfo");
        if (arrayInfo is not null)
        {
            var lbound = arrayInfo.ChildText("LBound") ?? "0";
            var elements = arrayInfo.ChildText("Elements");
            if (int.TryParse(elements, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) &&
                int.TryParse(lbound, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lo))
            {
                var hi = lo + count - 1;
                typeName = $"ARRAY [{lo}..{hi}] OF {typeName}";
            }
        }

        int? bitOffs = null;
        var bitText = sym.ChildText("BitOffs");
        if (int.TryParse(bitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOffs))
            bitOffs = parsedOffs;

        var iecRaw = FindIecAddress(sym);
        var iecDisplay = iecRaw is null ? null : FormatIecAddress(iecRaw);
        var addressDisplay = iecDisplay ?? FormatHexOffset(bitOffs);

        return new SymbolEntry
        {
            Name = name,
            TypeName = typeName,
            Direction = direction,
            AddressDisplay = addressDisplay,
            IecAddress = iecDisplay,
            BitOffset = bitOffs,
            SourceFile = sourceFile,
            DataArea = areaName
        };
    }

    private static string? FindIecAddress(XElement sym)
    {
        foreach (var props in sym.ElementsLocal("Properties"))
        {
            foreach (var prop in props.ElementsLocal("Property"))
            {
                var pname = prop.ChildText("Name") ?? prop.Attr("Name") ?? "";
                if (pname.Equals("TcAddress", StringComparison.OrdinalIgnoreCase) ||
                    pname.Equals("IecAddress", StringComparison.OrdinalIgnoreCase) ||
                    pname.Equals("Address", StringComparison.OrdinalIgnoreCase) ||
                    pname.Equals("AT", StringComparison.OrdinalIgnoreCase) ||
                    pname.Equals("IecAdr", StringComparison.OrdinalIgnoreCase))
                {
                    var value = prop.ChildText("Value") ?? prop.Attr("Value");
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }
        }

        var comment = sym.ChildText("Comment");
        if (!string.IsNullOrWhiteSpace(comment))
        {
            var token = comment.Trim().Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (token is not null && LooksLikeIec(token))
                return token;
        }

        return null;
    }

    internal static bool LooksLikeIec(string token)
    {
        var t = token.Trim();
        if (t.StartsWith("AT", StringComparison.OrdinalIgnoreCase))
            t = t[2..].Trim();
        return IecPattern.IsMatch(t);
    }

    /// <summary>
    /// Normalize IEC addresses to the compact TwinCAT display used in the UI (e.g. %IX0.0 → I0.0).
    /// </summary>
    public static string FormatIecAddress(string raw)
    {
        var t = raw.Trim();
        if (t.StartsWith("AT", StringComparison.OrdinalIgnoreCase))
            t = t[2..].Trim();
        if (t.StartsWith('%'))
            t = t[1..];

        // %IX0.0 / IX0.0 → I0.0; leave IB/IW/ID, QB, MW, etc. with the size letter.
        if (t.Length >= 2)
        {
            var area = char.ToUpperInvariant(t[0]);
            if (area is 'I' or 'Q' or 'M' or 'D')
            {
                var rest = t[1..];
                if (rest.StartsWith("X", StringComparison.OrdinalIgnoreCase))
                    return $"{area}{rest[1..]}";
                return area + rest;
            }
        }
        return t;
    }

    /// <summary>Format a TMC BitOffs (bits) as a hex byte offset, e.g. 0x00A0. Bit remainder as 0x00A0.1.</summary>
    public static string FormatHexOffset(int? bitOffs)
    {
        if (bitOffs is null)
            return "";
        var bits = bitOffs.Value;
        if (bits < 0)
            return "";
        var bytes = bits / 8;
        var rem = bits % 8;
        var hex = "0x" + bytes.ToString("X4", CultureInfo.InvariantCulture);
        return rem == 0 ? hex : $"{hex}.{rem}";
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string Sanitize(string message)
    {
        var first = message.Split('\n')[0].Trim();
        return first.Length > 240 ? first[..240] + "…" : first;
    }
}

using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using BootScanner.Core.Models;

namespace BootScanner.Core.Export;

/// <summary>
/// UTF-8 with BOM CSV export so Excel on Chinese Windows opens the file correctly.
/// </summary>
public static class CsvExporter
{
    /// <summary>
    /// Write the variable table to <paramref name="variablesPath"/>.
    /// If <paramref name="topologyPath"/> is null, writes a sibling file named {stem}-topology.csv.
    /// </summary>
    public static (string VariablesPath, string TopologyPath) Export(
        BootScanResult result,
        string variablesPath,
        string? topologyPath = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(variablesPath))
            throw new ArgumentException("变量 CSV 路径不能为空。", nameof(variablesPath));

        topologyPath ??= DeriveTopologyPath(variablesPath);
        if (CanonicalPath(variablesPath).Equals(CanonicalPath(topologyPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("变量表和拓扑表必须保存为不同文件。");
        ValidateOutput(result, variablesPath);
        ValidateOutput(result, topologyPath);

        var dir = Path.GetDirectoryName(Path.GetFullPath(variablesPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        WriteVariables(result, variablesPath);
        WriteTopology(result, topologyPath);
        return (variablesPath, topologyPath);
    }

    public static string DeriveTopologyPath(string variablesPath)
    {
        var dir = Path.GetDirectoryName(variablesPath) ?? "";
        var stem = Path.GetFileNameWithoutExtension(variablesPath);
        if (stem.EndsWith("-variables", StringComparison.OrdinalIgnoreCase))
            stem = stem[..^"-variables".Length];
        return Path.Combine(dir, stem + "-topology.csv");
    }

    public static void WriteVariables(BootScanResult result, string path)
    {
        ValidateOutput(result, path);
        var sb = new StringBuilder();
        sb.AppendLine("名称,类型,方向,地址/偏移,数据区,来源文件");
        foreach (var s in result.Symbols)
        {
            sb.Append(Escape(s.Name)).Append(',')
                .Append(Escape(s.TypeName)).Append(',')
                .Append(Escape(s.DirectionLabel)).Append(',')
                .Append(Escape(s.AddressDisplay)).Append(',')
                .Append(Escape(s.DataArea)).Append(',')
                .Append(Escape(s.SourceFile))
                .AppendLine();
        }
        WriteUtf8Bom(path, sb.ToString());
    }

    public static void WriteTopology(BootScanResult result, string path)
    {
        ValidateOutput(result, path);
        var sb = new StringBuilder();
        sb.AppendLine("路径,名称,类别,订货号,地址,来源文件");
        foreach (var root in result.Topology)
            WriteNode(sb, root, "");
        WriteUtf8Bom(path, sb.ToString());
    }

    private static void WriteNode(StringBuilder sb, TopologyNode node, string parentPath)
    {
        var caption = node.DisplayText;
        var path = string.IsNullOrEmpty(parentPath) ? caption : parentPath + "/" + caption;
        sb.Append(Escape(path)).Append(',')
            .Append(Escape(caption)).Append(',')
            .Append(Escape(KindLabel(node.Kind))).Append(',')
            .Append(Escape(node.OrderNumber)).Append(',')
            .Append(Escape(node.Address)).Append(',')
            .Append(Escape(node.SourceFile))
            .AppendLine();
        foreach (var child in node.Children)
            WriteNode(sb, child, path);
    }

    public static string KindLabel(TopologyKind kind) => kind switch
    {
        TopologyKind.Device => "设备",
        TopologyKind.Master => "主站",
        TopologyKind.Box => "从站",
        TopologyKind.Terminal => "端子",
        TopologyKind.Cpu => "CPU",
        TopologyKind.System => "系统",
        TopologyKind.Variable => "变量",
        _ => "其他"
    };

    public static string Escape(string? value)
    {
        var s = value ?? "";
        if (s.TrimStart() is { Length: > 0 } trimmed && "=+-@".Contains(trimmed[0]))
            s = "'" + s;
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    public static void WriteUtf8Bom(string path, string content)
    {
        var fullPath = Path.GetFullPath(path);
        var temp = Path.Combine(Path.GetDirectoryName(fullPath)!, ".boot-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file, new UTF8Encoding(true))) writer.Write(content);
            // Replace the directory entry, never truncate an existing hard-link target.
            File.Move(temp, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static void ValidateOutput(BootScanResult result, string path)
    {
        var fullPath = CanonicalPath(path);
        if (!string.IsNullOrWhiteSpace(result.BootDirectory))
        {
            var boot = CanonicalPath(result.BootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (fullPath.Equals(boot, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(boot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("只读保护：请将 CSV 保存到 Boot 目录之外。");
        }
        for (var current = fullPath; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("请勿通过文件链接或目录联接导出。");
    }

    private static string CanonicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (!OperatingSystem.IsWindows()) return full;
        var suffix = new Stack<string>();
        var current = full;
        while (!File.Exists(current) && !Directory.Exists(current))
        {
            suffix.Push(Path.GetFileName(current));
            current = Path.GetDirectoryName(current) ?? throw new ArgumentException("导出路径无效。");
        }
        using var handle = CreateFile(current, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException("无法打开导出路径进行规范化。");
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw new IOException("无法规范化导出路径。");
        current = buffer.ToString();
        if (current.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) current = @"\\" + current[8..];
        else if (current.StartsWith(@"\\?\", StringComparison.Ordinal)) current = current[4..];
        foreach (var name in suffix) current = Path.Combine(current, name);
        return current;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, int length, uint flags);
}

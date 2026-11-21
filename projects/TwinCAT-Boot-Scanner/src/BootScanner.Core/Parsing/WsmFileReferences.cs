using System.Text;
using BootScanner.Core.Models;
using System.Runtime.Versioning;

namespace BootScanner.Core.Parsing;

/// <summary>Limited string evidence, NOT a WSM object model or verified PLC association parser.</summary>
[SupportedOSPlatform("windows")]
internal static class WsmFileReferences
{
    public static TopologyNode? Read(CompoundStorage storage, string fileName)
    {
        try { return ReadCore(storage, fileName); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new TopologyNode
            {
                Name = "PLC 文件引用（WSM 候选不可读；不影响其他配置流）",
                Kind = TopologyKind.System,
                SourceFile = fileName + "#TcSysManager/WSM"
            };
        }
    }

    private static TopologyNode? ReadCore(CompoundStorage storage, string fileName)
    {
        if (!storage.TryOpenStream("WSM", out var stream)) return null;
        using (stream)
        {
            // Unknown encodings/long-string formats remain unsupported rather than guessed.
            if (stream!.Length > 16 * 1024 * 1024) return null;
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            var root = new TopologyNode { Name = "PLC 文件引用（WSM 字符串候选，关联未验证）", Kind = TopologyKind.System };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i + 2 < bytes.Length; i++)
            {
                if (!((bytes[i] >= 'A' && bytes[i] <= 'Z') || (bytes[i] >= 'a' && bytes[i] <= 'z'))
                    || bytes[i + 1] != ':' || (bytes[i + 2] != '\\' && bytes[i + 2] != '/')) continue;
                var length = bytes[i - 1];
                if (length < 7 || length == 255 || i + length > bytes.Length) continue;
                var value = bytes.AsSpan(i, length);
                if (value.IndexOfAnyExceptInRange((byte)32, (byte)126) >= 0) continue;
                var path = Encoding.ASCII.GetString(value);
                if (path.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*' }) >= 0) continue;
                var symbol = path.EndsWith(".tpy", StringComparison.OrdinalIgnoreCase);
                if (!symbol && !path.EndsWith(".pro", StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith(".plcproj", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(path)) continue;
                var source = fileName + "#TcSysManager/WSM@0x" + i.ToString("X");
                var basename = path.Replace('\\', '/').Split('/')[^1];
                var item = new TopologyNode { Name = (symbol ? "符号文件：" : "工程文件候选：") + basename, SourceFile = source };
                item.Children.Add(new() { Name = "记录路径（未访问）：" + path, SourceFile = source });
                item.Children.Add(new() { Name = "证据：长度匹配的 ASCII 字符串；未验证 PLC 工程关联，不由文件名推断工程名", SourceFile = source });
                root.Children.Add(item);
            }
            return root.Children.Count > 0 ? root : null;
        }
    }
}

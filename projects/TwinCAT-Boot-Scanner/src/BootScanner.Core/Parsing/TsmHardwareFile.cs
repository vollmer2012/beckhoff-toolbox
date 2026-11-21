namespace BootScanner.Core.Parsing;

/// <summary>
/// Identifies TwinCAT 2 CurrentConfig.tsm / .wsm. From TwinCAT 2.9 the .tsm is an OLE Structured Storage
/// document (optionally compressed). This is not XML and must not be fed to the CurrentConfig.xml parser.
/// </summary>
public enum TsmStorageKind
{
    OleStructuredStorage,
    PossiblyCompressedOrUnknown
}

public sealed class TsmInspection
{
    public required string FileName { get; init; }
    public required TsmStorageKind Kind { get; init; }
    public required string Detail { get; init; }
}

public static class TsmHardwareFile
{
    /// <summary>Compound document signature: D0 CF 11 E0 A1 B1 1A E1.</summary>
    public static readonly byte[] OleCompoundSignature =
        { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

    public static TsmInspection Inspect(string path)
    {
        var name = Path.GetFileName(path);
        var isWsm = name.EndsWith(".wsm", StringComparison.OrdinalIgnoreCase);
        var role = isWsm
            ? "TwinCAT 2 旧版系统管理器硬件配置（CurrentConfig.wsm）"
            : "TwinCAT 2 系统管理器硬件配置（CurrentConfig.tsm）";

        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[OleCompoundSignature.Length];
            var read = stream.Read(header, 0, header.Length);
            if (read >= OleCompoundSignature.Length && header.AsSpan().SequenceEqual(OleCompoundSignature))
            {
                return new TsmInspection
                {
                    FileName = name,
                    Kind = TsmStorageKind.OleStructuredStorage,
                    Detail = $"{role}，文件头为 OLE Structured Storage。第一版暂不解析复合文档流（亦可能再经压缩），不推断拓扑"
                };
            }

            return new TsmInspection
            {
                FileName = name,
                Kind = TsmStorageKind.PossiblyCompressedOrUnknown,
                Detail = $"{role}，但文件头不是未压缩的 Structured Storage 标识（可能已压缩或格式不同）。暂不解析，不推断拓扑"
            };
        }
        catch (Exception ex)
        {
            return new TsmInspection
            {
                FileName = name,
                Kind = TsmStorageKind.PossiblyCompressedOrUnknown,
                Detail = $"{role}，无法读取文件（{ex.Message}）。暂不解析，不推断拓扑"
            };
        }
    }
}

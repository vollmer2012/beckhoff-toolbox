namespace BootScanner.Core.Parsing;

/// <summary>
/// Resolves a user-selected path (file, Boot folder, or a parent such as TwinCAT\3.1) to a Boot directory.
/// Only looks at the given path and a small set of well-known children — never walks the whole disk.
/// </summary>
public static class BootFolderLocator
{
    private static readonly string[] ChildCandidates =
    {
        "Boot",
        Path.Combine("3.1", "Boot"),
        Path.Combine("TwinCAT", "3.1", "Boot"),
        Path.Combine("ProgramData", "Beckhoff", "TwinCAT", "3.1", "Boot"),
        Path.Combine("TwinCAT", "Boot"),
        "TwinCAT RT (x64)",
        "TwinCAT RT (x86)",
        Path.Combine("_Boot", "TwinCAT RT (x64)"),
        Path.Combine("_Boot", "TwinCAT RT (x86)")
    };

    public static bool LooksLikeBootDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !BootFileSystem.IsDirectory(directory))
            return false;
        if (BootFileSystem.IsFile(Path.Combine(directory, "CurrentConfig.xml")))
            return true;
        if (BootFileSystem.IsFile(Path.Combine(directory, "CurrentConfig.tszip")))
            return true;
        // TwinCAT 2 System Manager hardware project (fixed name CurrentConfig, suffix .tsm / older .wsm)
        if (BootFileSystem.IsFile(Path.Combine(directory, "CurrentConfig.tsm")) ||
            BootFileSystem.IsFile(Path.Combine(directory, "CurrentConfig.wsm")))
            return true;
        if (BootFileSystem.IsDirectory(Path.Combine(directory, "Plc")) ||
            BootFileSystem.IsDirectory(Path.Combine(directory, "PLC")) ||
            BootFileSystem.IsDirectory(Path.Combine(directory, "TcPlc")) ||
            BootFileSystem.IsDirectory(Path.Combine(directory, "Tmi")) ||
            BootFileSystem.IsDirectory(Path.Combine(directory, "TMI")))
            return true;
        try
        {
            return BootFileSystem.Files(directory, "*.tmc").Any()
                   || BootFileSystem.Files(directory, "*.tmi").Any()
                   || BootFileSystem.Files(directory, "*.tpy").Any()
                   || BootFileSystem.Files(directory, "TCPLC_P_*.wbp").Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Resolve <paramref name="inputPath"/> to a Boot directory.
    /// If a file is dropped, its parent directory is used.
    /// </summary>
    public static string? Resolve(string? inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            return null;

        var path = inputPath.Trim().Trim('"');
        if (BootFileSystem.IsFile(path))
            path = Path.GetDirectoryName(path) ?? path;
        if (!BootFileSystem.IsDirectory(path))
            return null;

        path = Path.GetFullPath(path);
        path = PreferBootRootOverPlcFolder(path);

        if (LooksLikeBootDirectory(path))
            return path;

        // Prefer TC3 Boot over TC2 when both exist under a TwinCAT root.
        foreach (var rel in ChildCandidates)
        {
            var candidate = Path.GetFullPath(Path.Combine(path, rel));
            if (LooksLikeBootDirectory(candidate))
                return candidate;
        }

        // One level down: e.g. user picked a parent that contains Boot or "TwinCAT RT (x64)".
        try
        {
            // Prefer a child literally named Boot, then TC3 runtime folders, then any match.
            var children = Directory.GetDirectories(path);
            foreach (var preferred in new[] { "Boot", "TwinCAT RT (x64)", "TwinCAT RT (x86)" })
            {
                var hit = children.FirstOrDefault(d =>
                    string.Equals(Path.GetFileName(d), preferred, StringComparison.OrdinalIgnoreCase) &&
                    LooksLikeBootDirectory(d));
                if (hit is not null)
                    return Path.GetFullPath(hit);
            }

            foreach (var child in children)
            {
                if (LooksLikeBootDirectory(child))
                    return Path.GetFullPath(child);
                var nestedBoot = Path.Combine(child, "Boot");
                if (LooksLikeBootDirectory(nestedBoot))
                    return Path.GetFullPath(nestedBoot);
            }
        }
        catch
        {
            // Permission or IO issues — just report no resolution.
        }

        return null;
    }

    /// <summary>
    /// If the user dropped a file inside Plc\, walk up to the Boot folder that contains CurrentConfig.xml.
    /// </summary>
    private static string PreferBootRootOverPlcFolder(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var parent = Directory.GetParent(path)?.FullName;
        if (parent is null)
            return path;

        var isPlcName = name.Equals("Plc", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("PLC", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("TcPlc", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("Tmi", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("TMI", StringComparison.OrdinalIgnoreCase);

        var parentHasConfig = BootFileSystem.IsFile(Path.Combine(parent, "CurrentConfig.xml"))
                              || BootFileSystem.IsFile(Path.Combine(parent, "CurrentConfig.tszip"))
                              || BootFileSystem.IsFile(Path.Combine(parent, "CurrentConfig.tsm"))
                              || BootFileSystem.IsFile(Path.Combine(parent, "CurrentConfig.wsm"));

        if (isPlcName && (parentHasConfig || LooksLikeBootDirectory(parent)))
            return parent;

        // A folder that only has tmc/tmi, whose parent has CurrentConfig.xml, is almost certainly the Plc subfolder.
        if (!BootFileSystem.IsFile(Path.Combine(path, "CurrentConfig.xml")) && parentHasConfig)
            return parent;

        return path;
    }
}

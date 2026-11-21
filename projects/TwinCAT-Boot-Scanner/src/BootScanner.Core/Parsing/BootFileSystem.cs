namespace BootScanner.Core.Parsing;

/// <summary>Offline discovery never follows file or directory reparse points.</summary>
internal static class BootFileSystem
{
    public static bool IsFile(string path) => File.Exists(path) && HasNoLinks(path);
    public static bool IsDirectory(string path) => Directory.Exists(path) && HasNoLinks(path);

    private static bool HasNoLinks(string path)
    {
        try
        {
            for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return false; }
    }

    public static IReadOnlyList<string> Files(string directory, string pattern = "*", bool recursive = false, CancellationToken cancellationToken = default)
    {
        if (!IsDirectory(directory)) return Array.Empty<string>();
        try
        {
            return Directory.EnumerateFiles(directory, pattern, new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                MatchType = MatchType.Simple
            }).Where(path => { cancellationToken.ThrowIfCancellationRequested(); return IsFile(path); }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return Array.Empty<string>(); }
    }
}

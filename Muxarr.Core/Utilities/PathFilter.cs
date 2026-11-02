namespace Muxarr.Core.Utilities;

public static class PathFilter
{
    // Standard local-extra folders used by media servers for sidecar videos.
    private static readonly string[] IgnoredMediaSidecarDirectories =
    [
        "Trailer",
        "Trailers",
        "Sample",
        "Samples"
    ];

    // Jellyfin's extras folders. "other" is left out on purpose: it is a
    // library category name too (/movies/other), and a false match there
    // would treat whole movies as extras. Trailers and samples are ignored
    // outright above.
    private static readonly string[] ExtrasDirectories =
    [
        "behind the scenes",
        "deleted scenes",
        "interviews",
        "scenes",
        "shorts",
        "featurettes",
        "clips",
        "extras",
        "backdrops"
    ];

    private static readonly string[] IgnoredFileNameSuffixes =
    [
        "trailer",
        "sample"
    ];

    private static readonly char[] FileNameTokenSeparators = ['-', '.', '_'];

    // OS/NAS directories that never contain real media, pre-wrapped with separators.
    private static readonly string[] IgnoredDirectories = new[]
    {
        "@eaDir", // Synology
        "#recycle", // Synology
        ".recycle", // Samba vfs_recycle
        "@Recycle", // QNAP
        ".@__thumb", // QNAP
        "$RECYCLE.BIN", // Windows
        "System Volume Information", // Windows
        "lost+found", // Linux
        ".Trash", // macOS / Linux
        ".AppleDouble", // macOS
        ".zfs" // ZFS snapshots
    }.Select(d => $"{Path.DirectorySeparatorChar}{d}{Path.DirectorySeparatorChar}").ToArray();

    public static bool ShouldIgnore(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        var fileName = GetFileName(normalizedPath);
        if (fileName.StartsWith("._", StringComparison.Ordinal))
        {
            return true;
        }

        if (HasIgnoredFileNameSuffix(fileName))
        {
            return true;
        }

        foreach (var dir in IgnoredDirectories)
        {
            if (normalizedPath.Contains(dir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (ContainsDirectorySegment(normalizedPath, IgnoredMediaSidecarDirectories))
        {
            return true;
        }

        return false;
    }

    /// <summary><see cref="ShouldIgnore(string)"/>, plus the user's own excluded folders.</summary>
    public static bool ShouldIgnore(string filePath, IReadOnlyCollection<string>? excludedFolders)
    {
        return ShouldIgnore(filePath) || IsExcluded(filePath, excludedFolders);
    }

    /// <summary>
    /// True when the file is under one of <paramref name="excludedFolders"/>. An entry
    /// starting with a slash (or a drive letter) is a full path and excludes that folder
    /// and everything below it; any other entry is a folder name, matched anywhere in
    /// the path, ignoring case.
    /// </summary>
    public static bool IsExcluded(string filePath, IReadOnlyCollection<string>? excludedFolders)
    {
        if (excludedFolders is not { Count: > 0 })
        {
            return false;
        }

        var normalizedPath = NormalizePath(filePath);
        var names = new List<string>();
        foreach (var raw in excludedFolders)
        {
            var entry = NormalizePath(raw.Trim()).TrimEnd(Path.DirectorySeparatorChar);
            if (entry.Length == 0)
            {
                continue;
            }

            if (IsFullPath(entry))
            {
                if (normalizedPath.StartsWith(entry + Path.DirectorySeparatorChar, PathComparison))
                {
                    return true;
                }
            }
            else
            {
                names.Add(entry);
            }
        }

        return names.Count > 0 && ContainsDirectorySegment(normalizedPath, names.ToArray());
    }

    /// <summary>One entry per line; blank lines and lines starting with # are skipped.</summary>
    public static List<string> ParseExcludedFolders(string? text)
    {
        return (text ?? string.Empty)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();
    }

    private static bool IsFullPath(string entry)
    {
        return entry.StartsWith(Path.DirectorySeparatorChar)
               || (entry.Length >= 2 && char.IsLetter(entry[0]) && entry[1] == ':');
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>True for a file inside one of Jellyfin's extras folders (featurettes, deleted scenes, ...).</summary>
    public static bool IsExtra(string filePath)
    {
        return ContainsDirectorySegment(NormalizePath(filePath), ExtrasDirectories);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string GetFileName(string normalizedPath)
    {
        var separatorIndex = normalizedPath.LastIndexOf(Path.DirectorySeparatorChar);
        return separatorIndex >= 0 ? normalizedPath[(separatorIndex + 1)..] : normalizedPath;
    }

    private static bool ContainsDirectorySegment(string normalizedPath, string[] directoryNames)
    {
        var segments = normalizedPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (directoryNames.Contains(segments[i], StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasIgnoredFileNameSuffix(string fileName)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        foreach (var suffix in IgnoredFileNameSuffixes)
        {
            if (string.Equals(nameWithoutExtension, suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!nameWithoutExtension.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separatorIndex = nameWithoutExtension.Length - suffix.Length - 1;
            if (separatorIndex >= 0 && FileNameTokenSeparators.Contains(nameWithoutExtension[separatorIndex]))
            {
                return true;
            }
        }

        return false;
    }
}

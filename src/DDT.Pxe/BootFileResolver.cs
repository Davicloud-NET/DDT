using System.Diagnostics.CodeAnalysis;

namespace DDT.Pxe;

// Resolves a client supplied name to a file inside the boot directory, or refuses. This is the most
// exposed surface DDT has: it is served over TFTP and over the plain HTTP boot listener, both
// anonymous, to anyone who can reach the segment. Both transports share this one resolver, because
// two resolvers means the path one of them rejects is served by the other.
public sealed class BootFileResolver
{
    private static readonly string[] s_reservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    private readonly string _root;

    public BootFileResolver(string bootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootDirectory);

        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(bootDirectory));
    }

    public string Root => _root;

    public bool TryResolve(string requested, [NotNullWhen(true)] out FileInfo? file)
    {
        file = null;

        if (string.IsNullOrWhiteSpace(requested) || !IsAcceptable(requested))
        {
            return false;
        }

        string candidate;

        try
        {
            candidate = Path.GetFullPath(Path.Combine(_root, requested.Replace('\\', '/')));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }

        if (!IsInsideRoot(candidate))
        {
            return false;
        }

        FileInfo resolved = new(candidate);

        if (!resolved.Exists || resolved.Attributes.HasFlag(FileAttributes.Directory))
        {
            return false;
        }

        // A junction or symlink placed inside the boot directory resolves to a path that passes the
        // containment check while pointing anywhere on the volume.
        if (HasReparsePointInChain(resolved))
        {
            return false;
        }

        file = resolved;

        return true;
    }

    private static bool IsAcceptable(string requested)
    {
        if (Path.IsPathRooted(requested) || requested.StartsWith("//", StringComparison.Ordinal)
            || requested.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (string segment in requested.Split(['/', '\\']))
        {
            if (!IsAcceptableSegment(segment))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAcceptableSegment(string segment)
    {
        if (segment.Length == 0 || segment is "." or "..")
        {
            return false;
        }

        // Windows silently strips trailing dots and spaces, so "boot.efi." and "boot.efi " name the
        // same file as "boot.efi" and would slip past an allowlist built on the requested string.
        if (segment != segment.Trim() || segment.EndsWith('.'))
        {
            return false;
        }

        if (segment.Contains(':', StringComparison.Ordinal)
            || segment.AsSpan().ContainsAny(Path.GetInvalidFileNameChars()))
        {
            return false;
        }

        string stem = Path.GetFileNameWithoutExtension(segment);

        return !s_reservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }

    private bool IsInsideRoot(string candidate)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return candidate.Length > _root.Length
            && candidate.StartsWith(_root, comparison)
            && candidate[_root.Length] == Path.DirectorySeparatorChar;
    }

    private bool HasReparsePointInChain(FileInfo file)
    {
        if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return true;
        }

        for (DirectoryInfo? directory = file.Directory;
             directory is not null && !PathEqualsRoot(directory.FullName);
             directory = directory.Parent)
        {
            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }

        return false;
    }

    private bool PathEqualsRoot(string path) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(path),
            _root,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

using System.Diagnostics.CodeAnalysis;

namespace DDT.Pxe;

// Resolves a client supplied name to a file inside the boot directory, or refuses. Served over TFTP
// and over the plain HTTP boot listener, both anonymous, so both transports share this one resolver:
// two resolvers means the path one of them rejects is served by the other.
//
// Every segment is matched against the names the directory actually holds rather than handed to the
// filesystem. That is what makes the string checks nearly redundant: "..", a rooted path, an 8.3
// short name, a trailing dot, an alternate data stream or a device name never equals a directory
// entry, so none of them can alias a file or leave the root.
public sealed class BootFileResolver
{
    private const int MaxRequestLength = 512;

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

        if (string.IsNullOrEmpty(requested) || requested.Length > MaxRequestLength)
        {
            return false;
        }

        // Windows boot components send backslashes and a leading separator: the boot manager asks for
        // "\Boot\BCD". The leading separator means the TFTP root, never the filesystem root.
        string[] segments = requested.Replace('\\', '/').TrimStart('/').Split('/');

        try
        {
            DirectoryInfo directory = new(_root);

            if (!directory.Exists)
            {
                return false;
            }

            for (int index = 0; index < segments.Length; index++)
            {
                FileSystemInfo? entry = FindEntry(directory, segments[index]);

                // A junction or symlink inside the boot directory can point anywhere on the volume.
                if (entry is null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return false;
                }

                bool last = index == segments.Length - 1;

                if (last && entry is FileInfo found)
                {
                    file = found;

                    return true;
                }

                if (last || entry is not DirectoryInfo child)
                {
                    return false;
                }

                directory = child;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    // An exact match wins. Otherwise a single case insensitive match is accepted, because firmware
    // written against case insensitive Windows servers asks for "\Boot\BCD" whatever the file on a
    // Linux host is called. Two names differing only in case are ambiguous and refused.
    private static FileSystemInfo? FindEntry(DirectoryInfo directory, string name)
    {
        if (name.Length == 0 || name is "." or "..")
        {
            return null;
        }

        FileSystemInfo? match = null;
        bool ambiguous = false;

        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
        {
            if (string.Equals(entry.Name, name, StringComparison.Ordinal))
            {
                return entry;
            }

            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                ambiguous = match is not null;
                match = entry;
            }
        }

        return ambiguous ? null : match;
    }
}

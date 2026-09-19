using System.Security.Cryptography;

namespace DDT.Agent.Deployment;

// The agent carries libwim-15.dll as a resource, so one executable is all the boot image and the self-update
// deliver. The runtime looks for the library next to the executable, which is where it is written.
public static class WimLibraryFile
{
    public const string FileName = "libwim-15.dll";

    // Writes the library when it is missing or differs, for example one left behind by an older agent, and
    // returns its full path. Must run before the first wimlib call: a loaded library cannot be replaced.
    public static string EnsureExtracted(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        string path = Path.GetFullPath(Path.Combine(directory, FileName));
        byte[] library = ReadResource();

        if (File.Exists(path) && SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(SHA256.HashData(library)))
        {
            return path;
        }

        Directory.CreateDirectory(directory);

        string partial = path + ".part";
        File.WriteAllBytes(partial, library);
        File.Move(partial, path, overwrite: true);

        return path;
    }

    private static byte[] ReadResource()
    {
        using Stream resource = typeof(WimLibraryFile).Assembly.GetManifestResourceStream(FileName)
            ?? throw new InvalidOperationException($"This agent was built without {FileName}.");
        using MemoryStream copy = new();
        resource.CopyTo(copy);

        return copy.ToArray();
    }
}

using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace DDT.Core.Tests.Wim;

public static class TestTree
{
    public const int RandomBytes = 1_500_000;

    // Files, a subdirectory, an empty file, a non-ASCII name and a hard link.
    [SupportedOSPlatform("windows")]
    public static void Create(string root)
    {
        string random = Path.Combine(root, "sub", "random.bin");

        Directory.CreateDirectory(Path.Combine(root, "sub", "deeper"));
        File.WriteAllText(Path.Combine(root, "hello.txt"), "hello wim");
        File.WriteAllBytes(random, RandomNumberGenerator.GetBytes(RandomBytes));
        File.WriteAllText(Path.Combine(root, "sub", "repeated.txt"), string.Concat(Enumerable.Repeat("a line that compresses well\n", 40_000)));
        File.WriteAllBytes(Path.Combine(root, "sub", "deeper", "empty.dat"), []);
        File.WriteAllText(Path.Combine(root, "Grüße ümlaut €.txt"), "unicode name");
        HardLinks.Create(Path.Combine(root, "sub", "hardlink.bin"), random);
    }

    // Every directory and file below the root, files with their size and SHA-256, in a stable order.
    public static IReadOnlyList<string> Describe(string root) =>
    [
        .. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path => Describe(root, path))
            .Order(StringComparer.Ordinal),
    ];

    public static long TotalFileBytes(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);

    private static string Describe(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path).Replace('\\', '/');

        return Directory.Exists(path)
            ? relative + "/"
            : $"{relative} {new FileInfo(path).Length} {Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))}";
    }
}

using System.Security.Cryptography;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class WimLibraryFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ddt-wimlib-").FullName;

    // The build copies the package's libwim-15.dll next to the tests, byte for byte.
    private static string PackageFile => Path.Combine(AppContext.BaseDirectory, WimLibraryFile.FileName);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void WritesTheLibraryTheAgentCarries()
    {
        string path = WimLibraryFile.EnsureExtracted(_directory);

        Assert.Equal(Path.Combine(_directory, WimLibraryFile.FileName), path);
        Assert.Equal(Sha256(PackageFile), Sha256(path));
        Assert.Equal([WimLibraryFile.FileName], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public void ReplacesADifferentLibraryAndKeepsTheSameOne()
    {
        string path = Path.Combine(_directory, WimLibraryFile.FileName);
        File.WriteAllBytes(path, [1, 2, 3]);

        WimLibraryFile.EnsureExtracted(_directory);
        Assert.Equal(Sha256(PackageFile), Sha256(path));

        DateTime written = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);
        WimLibraryFile.EnsureExtracted(_directory);

        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    private static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}

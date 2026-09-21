using System.Security.Cryptography;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class WimLibraryFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ddt-wimlib-").FullName;

    // The build copies the package's libwim-15.dll next to the tests, byte for byte.
    private static string PackageFile => Path.Combine(AppContext.BaseDirectory, WimLibraryFile.FileName);

    private string LibraryPath => Path.Combine(_directory, WimLibraryFile.FileName);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void WritesTheLibraryTheAgentCarriesWhereThereIsNone()
    {
        WimLibraryInUse library = WimLibraryFile.EnsureExtracted(_directory);

        Assert.Equal(new WimLibraryInUse(LibraryPath, Sha256(PackageFile), Sha256(PackageFile)), library);
        Assert.True(library.IsCarriedCopy);
        Assert.Equal(Sha256(PackageFile), Sha256(LibraryPath));
        Assert.Equal([WimLibraryFile.FileName], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public void UsesAnIdenticalLibraryWithoutWritingIt()
    {
        File.Copy(PackageFile, LibraryPath);
        DateTime written = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(LibraryPath, written);

        WimLibraryInUse library = WimLibraryFile.EnsureExtracted(_directory);

        Assert.True(library.IsCarriedCopy);
        Assert.Equal(LibraryPath, library.Path);
        Assert.Equal(written, File.GetLastWriteTimeUtc(LibraryPath));
    }

    [Fact]
    public void KeepsADifferentLibraryAndReportsBothHashes()
    {
        byte[] own = [1, 2, 3];
        File.WriteAllBytes(LibraryPath, own);

        WimLibraryInUse library = WimLibraryFile.EnsureExtracted(_directory);

        Assert.Equal(own, File.ReadAllBytes(LibraryPath));
        Assert.False(library.IsCarriedCopy);
        Assert.Equal(new WimLibraryInUse(LibraryPath, Convert.ToHexStringLower(SHA256.HashData(own)), Sha256(PackageFile)), library);
        Assert.Equal([WimLibraryFile.FileName], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    private static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}

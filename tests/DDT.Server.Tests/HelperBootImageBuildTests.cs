// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using DDT.Host.Helper;
using DDT.Pxe;
using DDT.Server.BootImage;
using Xunit;

namespace DDT.Server.Tests;

// What the helper does with a build request as SYSTEM, with a process of the test's own in place of PowerShell.
public sealed class HelperBootImageBuildTests : IDisposable
{
    private const string Name = "20261001-100000";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ddt-helper-" + Guid.NewGuid().ToString("N"));
    private readonly HelperPaths _paths;
    private readonly List<string> _lines = [];
    private readonly List<string> _arguments = [];
    private int _exitCode;
    private string[] _driverFiles = [];

    public HelperBootImageBuildTests()
    {
        _paths = new HelperPaths(Path.Combine(_root, "program"), Path.Combine(_root, "store"), Path.Combine(_root, "store", "boot"), Path.Combine(_root, "work"));
        Directory.CreateDirectory(_paths.ProgramFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.ObjectPath("x")) ?? _root);
        File.WriteAllText(_paths.Script, "# the release's script");
    }

    [Fact]
    public async Task ItRunsTheReleasesScriptWithTheCheckedValuesAndServesWhatItBuilt()
    {
        HelperDriver driver = Package("Network drivers", "e1d.inf");
        HelperRequest request = Request() with
        {
            KeyboardLayout = "0407:00000407",
            SkipPowerShell = true,
            TftpWindowSize = 8,
            Drivers = [driver],
            DriverSetHash = new string('a', 64),
        };

        Assert.Null(await Build().RunAsync(request, _lines.Add, TestContext.Current.CancellationToken));

        string work = Path.Combine(_paths.WorkRoot, Name);
        Assert.Equal(
            [
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", _paths.Script,
                "-Destination", Path.Combine(work, "out"),
                "-WorkDirectory", Path.Combine(work, "winpe"),
                "-ServerUrl", "https://deploy01.contoso.local:8443",
                "-KeyboardLayout", "0407:00000407",
                "-TftpWindowSize", "8",
                "-SkipPowerShell",
                "-ServerDriverPath", Path.Combine(work, "drivers"),
            ],
            _arguments);
        Assert.Equal(
            new[] { DriverPackages.ListName, Path.Combine(driver.PackageId.ToString("D"), "e1d.inf") }.Order(StringComparer.Ordinal),
            _driverFiles);

        Assert.Equal(Name, BootBuilds.Current(_paths.BootDirectory));
        Assert.Equal("the image", File.ReadAllText(Path.Combine(BootBuilds.Serving(_paths.BootDirectory), "Boot", "boot.wim")));
        Assert.True(File.Exists(Path.Combine(BootBuilds.Serving(_paths.BootDirectory), "x64", "bootmgfw.efi")));
        Assert.False(Directory.Exists(work));
        Assert.Contains($"The server serves this build, {Name}, from now on.", _lines);
    }

    [Theory]
    [InlineData("../elsewhere", "https://deploy01:8443", null, null)]
    [InlineData(Name, "http://deploy01:8443", null, null)]
    [InlineData(Name, "https://deploy01:8443/path", null, null)]
    [InlineData(Name, "https://someone@deploy01:8443", null, null)]
    [InlineData(Name, "https://deploy01:8443\" -Evil", null, null)]
    [InlineData(Name, "https://deploy01:8443", "German; calc", null)]
    [InlineData(Name, "https://deploy01:8443", null, 0)]
    public async Task ARequestThatSaysAnythingElseRunsNothing(string name, string serverUrl, string? keyboardLayout, int? windowSize)
    {
        HelperRequest request = new() { Kind = HelperRequest.Build, Name = name, ServerUrl = serverUrl, KeyboardLayout = keyboardLayout, TftpWindowSize = windowSize };

        Assert.NotNull(await Build().RunAsync(request, _lines.Add, TestContext.Current.CancellationToken));

        Assert.Empty(_arguments);
        Assert.False(Directory.Exists(_paths.BootDirectory));
    }

    [Fact]
    public async Task ADriverPackageThatIsNotTheFileItsNameSaysStopsTheBuild()
    {
        HelperDriver driver = Package("Network drivers", "e1d.inf");
        File.AppendAllText(_paths.ObjectPath(driver.Sha256), "changed");

        string? problem = await Build().RunAsync(Request() with { Drivers = [driver] }, _lines.Add, TestContext.Current.CancellationToken);

        Assert.Contains("is not the file its name says", problem, StringComparison.Ordinal);
        Assert.Empty(_arguments);

        string? unnamed = await Build().RunAsync(Request() with { Drivers = [driver with { Sha256 = "..\\..\\secret" }] }, _lines.Add, TestContext.Current.CancellationToken);
        Assert.Contains("names a driver package that is none", unnamed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AScriptThatFailsPublishesNothingAndABuildIsNeverWrittenOver()
    {
        _exitCode = 1;

        string? failed = await Build().RunAsync(Request(), _lines.Add, TestContext.Current.CancellationToken);

        Assert.Contains("exit code 1", failed, StringComparison.Ordinal);
        Assert.Null(BootBuilds.Current(_paths.BootDirectory));
        Assert.Empty(BootBuilds.List(_paths.BootDirectory));
        Assert.False(Directory.Exists(Path.Combine(_paths.WorkRoot, Name)));

        _exitCode = 0;
        Assert.Null(await Build().RunAsync(Request(), _lines.Add, TestContext.Current.CancellationToken));
        Assert.Contains("has a build", await Build().RunAsync(Request(), _lines.Add, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private HelperBootImageBuild Build() => new(_paths, RunAsync);

    private static HelperRequest Request() =>
        new() { Kind = HelperRequest.Build, Name = Name, ServerUrl = "https://deploy01.contoso.local:8443/" };

    // A driver package in the store, under its hash.
    private HelperDriver Package(string name, string file)
    {
        using MemoryStream zip = new();

        using (ZipArchive archive = new(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using StreamWriter entry = new(archive.CreateEntry(file).Open());
            entry.Write("[Version]");
        }

        string sha256 = Convert.ToHexStringLower(SHA256.HashData(zip.ToArray()));
        File.WriteAllBytes(_paths.ObjectPath(sha256), zip.ToArray());

        return new HelperDriver(Guid.NewGuid(), name, sha256);
    }

    // Stands in for PowerShell: notes its arguments and what the drivers folder holds, and leaves a build behind.
    private Task<int> RunAsync(ProcessStartInfo start, Action<string> line, CancellationToken cancellationToken)
    {
        _arguments.AddRange(start.ArgumentList);
        line("Building");

        int drivers = start.ArgumentList.IndexOf("-ServerDriverPath");

        if (drivers >= 0)
        {
            string folder = start.ArgumentList[drivers + 1];
            _driverFiles = [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(folder, file)).Order(StringComparer.Ordinal)];
        }

        if (_exitCode == 0)
        {
            string built = start.ArgumentList[start.ArgumentList.IndexOf("-Destination") + 1];
            Directory.CreateDirectory(Path.Combine(built, "Boot"));
            Directory.CreateDirectory(Path.Combine(built, "x64"));
            File.WriteAllText(Path.Combine(built, "Boot", "boot.wim"), "the image");
            File.WriteAllText(Path.Combine(built, "x64", "bootmgfw.efi"), "the boot manager");
        }

        return Task.FromResult(_exitCode);
    }
}

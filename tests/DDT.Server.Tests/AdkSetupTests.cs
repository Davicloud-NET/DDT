// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Host.Startup;
using Xunit;

namespace DDT.Server.Tests;

public sealed class AdkSetupTests : IDisposable
{
    private const string Kit = "Assessment and Deployment Kit";

    private static readonly AdkPart s_tools = Part("Windows ADK", "adksetup.exe", "OptionId.DeploymentTools", "tools.marker");
    private static readonly AdkPart s_addOn = Part("Windows PE add-on", "adkwinpesetup.exe", "OptionId.WindowsPreinstallationEnvironment", "pe.marker");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ddt-adk-" + Guid.NewGuid().ToString("N"));
    private readonly FakeMachine _machine;

    public AdkSetupTests()
    {
        _machine = new FakeMachine(Path.Combine(_folder, "kits"));
    }

    [Fact]
    public void WithBothPartsThereNothingIsDownloaded()
    {
        _machine.Has(s_tools);
        _machine.Has(s_addOn);
        using StringWriter output = new();

        Assert.Null(Setup().Install(output));

        Assert.Empty(_machine.Calls);
        Assert.Contains("installed already", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AFreshServerGetsBothSetupsAndRunsThemQuietly()
    {
        using StringWriter output = new();

        Assert.Null(Setup().Install(output));

        string downloads = Path.Combine(_folder, "downloads");
        Assert.Equal(
            [
                "wait",
                "download https://download.example/adksetup.exe",
                $"adksetup /quiet /norestart /ceip off /features OptionId.DeploymentTools /log {Path.Combine(downloads, "adksetup.log")}",
                "download https://download.example/adkwinpesetup.exe",
                $"adkwinpesetup /quiet /norestart /ceip off /features OptionId.WindowsPreinstallationEnvironment /log {Path.Combine(downloads, "adkwinpesetup.log")}",
            ],
            _machine.Calls);
        Assert.False(Directory.Exists(downloads));
        Assert.Contains("Installed the Windows PE add-on.", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ADownloadThatIsNotThePinnedFileDoesNotRun()
    {
        _machine.Tampered = true;
        using StringWriter output = new();

        string? problem = Setup().Install(output);

        Assert.Contains("isn't the file this release of DDT was tried with", problem, StringComparison.Ordinal);
        Assert.DoesNotContain(_machine.Calls, call => call.StartsWith("adksetup", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "downloads")));
    }

    [Fact]
    public void ADownloadThatFailsSaysSoAndRunsNothing()
    {
        _machine.Offline = true;
        using StringWriter output = new();

        string? problem = Setup().Install(output);

        Assert.StartsWith("adksetup.exe didn't download from download.example", problem, StringComparison.Ordinal);
        Assert.Equal(["wait", "download https://download.example/adksetup.exe"], _machine.Calls);
    }

    [Fact]
    public void AFailedSetupNamesItsLogAndStopsBeforeTheAddOn()
    {
        _machine.ExitCode = 1603;
        using StringWriter output = new();

        string? problem = Setup().Install(output);

        Assert.StartsWith("adksetup.exe ended with exit code 1603", problem, StringComparison.Ordinal);
        Assert.EndsWith($"Its log is {Path.Combine(_folder, "downloads", "adksetup.log")}.", problem, StringComparison.Ordinal);
        Assert.DoesNotContain(_machine.Calls, call => call.Contains("adkwinpesetup", StringComparison.Ordinal));
    }

    [Fact]
    public void TheMissingAddOnOfTheSameAdkIsAdded()
    {
        _machine.Has(s_tools);
        _machine.Version = "1.0";
        using StringWriter output = new();

        Assert.Null(Setup().Install(output));

        Assert.Equal(3, _machine.Calls.Count);
        Assert.StartsWith("adkwinpesetup ", _machine.Calls[2], StringComparison.Ordinal);
    }

    [Fact]
    public void AnAddOnIsNotAddedToAnotherAdk()
    {
        _machine.Has(s_tools);
        _machine.Version = "0.9";
        using StringWriter output = new();

        string? problem = Setup().Install(output);

        Assert.StartsWith("This server has the Windows ADK 0.9", problem, StringComparison.Ordinal);
        Assert.Empty(_machine.Calls);
    }

    [Fact]
    public void TheReleaseNamesMicrosoftsSetupsWithTheirHashes()
    {
        Assert.Equal(["adksetup.exe", "adkwinpesetup.exe"], AdkSetup.Release.Select(part => part.File));
        Assert.All(AdkSetup.Release, part =>
        {
            Assert.Equal("https", part.Address.Scheme);
            Assert.Equal("download.microsoft.com", part.Address.Host);
            Assert.EndsWith("/" + part.File, part.Address.AbsolutePath, StringComparison.Ordinal);
            Assert.Matches("^[0-9A-F]{64}$", part.Sha256);
        });
        Assert.Single(AdkSetup.Release.Select(part => part.Version).Distinct());
    }

    [Fact]
    public void SetupAdkExitsWith1AndSaysWhatStoppedIt()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The setup verbs run on Windows only.");
        _machine.Offline = true;
        using StringWriter output = new();

        Assert.Equal(1, SetupConsole.Run(["setup", "adk"], output, adk: Setup()));
        Assert.Contains("didn't download", output.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private AdkSetup Setup() => new(_machine, Path.Combine(_folder, "downloads"), [s_tools, s_addOn]);

    private static AdkPart Part(string name, string file, string feature, string marker) =>
        new(name, "1.0", file, new Uri("https://download.example/" + file), Convert.ToHexString(SHA256.HashData(Content(file))), feature, marker);

    private static byte[] Content(string file) => System.Text.Encoding.UTF8.GetBytes("setup " + file);

    // Records every call. A setup that exits with 0 leaves its part's marker, as Microsoft's leaves the feature.
    private sealed class FakeMachine(string root) : IAdkMachine
    {
        public List<string> Calls { get; } = [];

        public string? Version { get; set; }

        public int ExitCode { get; set; }

        public bool Offline { get; set; }

        public bool Tampered { get; set; }

        public void Has(AdkPart part)
        {
            Directory.CreateDirectory(Path.Combine(root, Kit));
            File.WriteAllText(Path.Combine(root, Kit, part.Marker), string.Empty);
        }

        public string? KitsRoot() => Directory.Exists(root) ? root : null;

        public string? AdkVersion() => Version;

        public void WaitForWindowsInstaller() => Calls.Add("wait");

        public void Download(Uri address, string path)
        {
            Calls.Add($"download {address}");

            if (Offline)
            {
                throw new HttpRequestException("No such host is known.");
            }

            File.WriteAllBytes(path, Tampered ? [1, 2, 3] : Content(Path.GetFileName(path)));
        }

        public CommandResult Run(string program, IReadOnlyList<string> arguments)
        {
            Calls.Add($"{Path.GetFileNameWithoutExtension(program)} {string.Join(' ', arguments)}");

            if (ExitCode == 0)
            {
                Has(new[] { s_tools, s_addOn }.Single(part => part.File == Path.GetFileName(program)));
            }

            return new CommandResult(ExitCode, string.Empty);
        }
    }
}

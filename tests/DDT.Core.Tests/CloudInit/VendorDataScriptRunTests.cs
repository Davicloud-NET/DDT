// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.Versioning;
using DDT.Core.CloudInit;
using Xunit;

namespace DDT.Core.Tests.CloudInit;

// The vendor-data script, run by sh in a folder that stands in for the installed Linux: every way it ends, what it
// says then and what it leaves. Its text is the shipped one. Only a real image shows that cloud-init runs it.
[SupportedOSPlatform("linux")]
public sealed class VendorDataScriptRunTests : IDisposable
{
    private const string OnlyLinux = "The script is run by sh with tools a Linux has.";

    private const string Token = "run-token-of-the-sandbox";

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerFolder = OwnerOnly | UnixFileMode.UserExecute;
    private const UnixFileMode ForAll = OwnerFolder | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private static readonly (string Path, string Content)[] s_run =
    [
        (LinuxAgentFiles.SeedAgent, "#!/bin/sh\necho agent\n"),
        (LinuxAgentFiles.SeedConfiguration, "{\"serverUrl\":\"https://ddt.example:8443/\"}"),
        (LinuxAgentFiles.SeedState, "{\"phase\":\"Linux\"}"),
        (LinuxAgentFiles.SeedToken, Token),
    ];

    private VendorDataSandbox? _sandbox;

    public void Dispose() => _sandbox?.Dispose();

    private VendorDataSandbox Sandbox()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), OnlyLinux);

        return _sandbox = new VendorDataSandbox();
    }

    private static string Installed(VendorDataSandbox sandbox, string path) => sandbox.In(path.TrimStart('/'));

    private static void AssertNothingInstalled(VendorDataSandbox sandbox)
    {
        Assert.False(Directory.Exists(Installed(sandbox, LinuxAgentFiles.StateFolder)));
        Assert.False(File.Exists(Installed(sandbox, LinuxAgentFiles.Unit)));
        Assert.DoesNotContain(sandbox.Calls, call => call.StartsWith("systemctl", StringComparison.Ordinal));
    }

    [Fact]
    public void InstallsTheAgentFromTheSeedWipesTheTokenAndStartsTheUnit()
    {
        VendorDataSandbox sandbox = Sandbox();
        string device = sandbox.AddSeed("seed-a", byLabel: true, s_run);

        (int exitCode, string[] output, string[] errors) = sandbox.Run();

        Assert.Empty(errors);
        Assert.Equal(0, exitCode);
        Assert.Equal(
            [
                $"DDT: the agent is installed from {sandbox.In("dev/disk/by-label/CIDATA")} and the run's token is off the seed.",
                "DDT: ddt-agent.service is started. It waits for cloud-init, goes on with the task sequence and removes itself at the end.",
            ],
            output);
        Assert.NotEqual(device, sandbox.In("dev/disk/by-label/CIDATA"));

        Assert.Equal("#!/bin/sh\necho agent\n", File.ReadAllText(Installed(sandbox, LinuxAgentFiles.Agent)));
        Assert.Equal("{\"serverUrl\":\"https://ddt.example:8443/\"}", File.ReadAllText(Installed(sandbox, LinuxAgentFiles.Configuration)));
        Assert.Equal("{\"phase\":\"Linux\"}", File.ReadAllText(Installed(sandbox, LinuxAgentFiles.State)));
        Assert.Equal(Token, File.ReadAllText(Installed(sandbox, LinuxAgentFiles.Token)));
        Assert.Equal(ForAll, File.GetUnixFileMode(Installed(sandbox, LinuxAgentFiles.Agent)));
        Assert.Equal(ForAll, File.GetUnixFileMode(Installed(sandbox, LinuxAgentFiles.ProgramFolder)));
        Assert.Equal(OwnerFolder, File.GetUnixFileMode(Installed(sandbox, LinuxAgentFiles.StateFolder)));
        Assert.Equal(OwnerFolder, File.GetUnixFileMode(Installed(sandbox, LinuxAgentFiles.StateFolder + "/run")));
        Assert.All(
            (string[])[LinuxAgentFiles.Configuration, LinuxAgentFiles.State, LinuxAgentFiles.Token],
            path => Assert.Equal(OwnerOnly, File.GetUnixFileMode(Installed(sandbox, path))));

        // The unit is the text the script holds, whatever the folder it ran in.
        string unit = File.ReadAllText(Installed(sandbox, LinuxAgentFiles.Unit));
        Assert.Contains($"ExecStart={LinuxAgentFiles.Agent} {LinuxAgentFiles.Argument}\n", unit, StringComparison.Ordinal);
        Assert.DoesNotContain(sandbox.Root, unit, StringComparison.Ordinal);

        // The token leaves the seed only after the unit is enabled, and the seed is let go of at the end.
        Assert.Equal(
            [
                $"mount -t vfat -o rw,umask=077 {sandbox.In("dev/disk/by-label/CIDATA")} {sandbox.SeedMount}",
                "systemctl daemon-reload",
                "systemctl enable ddt-agent.service",
                "systemctl start --no-block ddt-agent.service",
                $"umount {sandbox.SeedMount}",
            ],
            sandbox.Calls);
        Assert.False(File.Exists(Path.Combine(sandbox.Volume("seed-a"), LinuxAgentFiles.SeedToken)));
        Assert.True(File.Exists(Path.Combine(sandbox.Volume("seed-a"), LinuxAgentFiles.SeedAgent)));
        Assert.False(Directory.Exists(sandbox.SeedMount));
    }

    // cloud-init runs its scripts again for a new instance id. By then the agent took the token and removed itself.
    [Fact]
    public void ASeedWithoutATokenEndsWellInstallsNothingAndLeavesNoMountPoint()
    {
        VendorDataSandbox sandbox = Sandbox();
        sandbox.AddSeed("seed-a", byLabel: true, (LinuxAgentFiles.SeedAgent, "agent"));

        (int exitCode, string[] output, string[] errors) = sandbox.Run();

        Assert.Empty(errors);
        Assert.Equal(0, exitCode);
        Assert.Equal(["DDT: no seed holds a run to go on with, so nothing was installed."], output);
        Assert.False(Directory.Exists(sandbox.SeedMount));
        AssertNothingInstalled(sandbox);
    }

    // Nothing is mounted when the script ends, so the unmount in its exit trap fails. The trap goes on all the same.
    [Fact]
    public void WithNoSeedToMountItEndsWellAndLeavesNoMountPoint()
    {
        VendorDataSandbox sandbox = Sandbox();

        (int exitCode, string[] output, string[] errors) = sandbox.Run();

        Assert.Empty(errors);
        Assert.Equal(0, exitCode);
        Assert.Equal(["DDT: no seed holds a run to go on with, so nothing was installed."], output);
        Assert.Equal([$"umount {sandbox.SeedMount}"], sandbox.Calls);
        Assert.False(Directory.Exists(sandbox.SeedMount));
        AssertNothingInstalled(sandbox);
    }

    [Fact]
    public void ASeedWithoutTheAgentFailsWithTheSentenceAndKeepsItsToken()
    {
        VendorDataSandbox sandbox = Sandbox();
        sandbox.AddSeed("seed-a", byLabel: true, s_run[1..]);

        (int exitCode, string[] output, string[] errors) = sandbox.Run();

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Equal("DDT: the agent could not be installed (exit code 1), so the run does not go on in this Linux.", errors[^1]);
        Assert.Contains("ddt-agent", errors[0], StringComparison.Ordinal);
        Assert.Equal(Token, File.ReadAllText(Path.Combine(sandbox.Volume("seed-a"), LinuxAgentFiles.SeedToken)));
        Assert.False(Directory.Exists(sandbox.SeedMount));
        Assert.False(File.Exists(Installed(sandbox, LinuxAgentFiles.Unit)));
        Assert.DoesNotContain(sandbox.Calls, call => call.StartsWith("systemctl", StringComparison.Ordinal));
    }

    // udev's link names one volume with the label. The run is on the other, which blkid lists too.
    [Fact]
    public void WithTwoSeedsItInstallsFromTheOneThatHoldsTheToken()
    {
        VendorDataSandbox sandbox = Sandbox();
        sandbox.AddSeed("seed-a", byLabel: true);
        string second = sandbox.AddSeed("seed-b", byLabel: false, s_run);

        (int exitCode, string[] output, string[] errors) = sandbox.Run();

        Assert.Empty(errors);
        Assert.Equal(0, exitCode);
        Assert.Equal($"DDT: the agent is installed from {second} and the run's token is off the seed.", output[0]);
        Assert.Equal(Token, File.ReadAllText(Installed(sandbox, LinuxAgentFiles.Token)));
        Assert.Equal([CloudInitSeed.UserData], Directory.GetFileSystemEntries(sandbox.Volume("seed-a")).Select(Path.GetFileName));
        Assert.False(File.Exists(Path.Combine(sandbox.Volume("seed-b"), LinuxAgentFiles.SeedToken)));

        // The link's volume is tried first and let go of, and blkid's first is the same volume again.
        Assert.Equal(3, sandbox.Calls.Count(call => call.StartsWith("mount ", StringComparison.Ordinal)));
        Assert.Equal(3, sandbox.Calls.Count(call => call.StartsWith("umount ", StringComparison.Ordinal)));
        Assert.False(Directory.Exists(sandbox.SeedMount));
    }

    // The agent is installed all the same, and overwrites the token on the seed when it starts.
    [Fact]
    public void ATokenThatCannotBeWipedIsSaidAndTheUnitStartsAllTheSame()
    {
        VendorDataSandbox sandbox = Sandbox();
        sandbox.AddSeed("seed-a", byLabel: true, s_run);
        sandbox.Tool("dd", "#!/bin/sh\nexit 1");

        (int exitCode, string[] output, string[] errors) = sandbox.Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(
            [
                $"DDT: the agent is installed from {sandbox.In("dev/disk/by-label/CIDATA")}, but the run's token could not be removed from the seed. The agent tries again.",
            ],
            errors);
        Assert.Equal(
            ["DDT: ddt-agent.service is started. It waits for cloud-init, goes on with the task sequence and removes itself at the end."],
            output);
        Assert.Equal(Token, File.ReadAllText(Path.Combine(sandbox.Volume("seed-a"), LinuxAgentFiles.SeedToken)));
        Assert.Equal(Token, File.ReadAllText(Installed(sandbox, LinuxAgentFiles.Token)));
        Assert.Equal("systemctl start --no-block ddt-agent.service", sandbox.Calls[^2]);
        Assert.False(Directory.Exists(sandbox.SeedMount));
    }

    [Fact]
    public void AnImageThatDoesNotRunSystemdIsRefusedBeforeAnythingIsMounted()
    {
        VendorDataSandbox sandbox = Sandbox();
        sandbox.AddSeed("seed-a", byLabel: true, s_run);
        Directory.Delete(sandbox.In("run/systemd/system"));

        (int exitCode, string[] output, string[] errors) = sandbox.Run();

        Assert.Equal(1, exitCode);
        Assert.Empty(output);
        Assert.Equal(["DDT: this image does not run systemd, so the run cannot go on in it."], errors);
        Assert.Empty(sandbox.Calls);
        Assert.False(Directory.Exists(sandbox.SeedMount));
        Assert.Equal(Token, File.ReadAllText(Path.Combine(sandbox.Volume("seed-a"), LinuxAgentFiles.SeedToken)));
    }
}

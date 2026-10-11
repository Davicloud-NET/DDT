// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

// A step's shares in the installed Linux, with the kernel's mount call faked. The real mount of a real server is for
// the run on a machine.
public sealed class LinuxShareConnectorTests : IDisposable
{
    // Made up, with what the kernel's option parser could trip over. It must never show in a log or a message.
    private const string Password = @"Pa55,w0rd\ with,,commas";

    private static readonly Guid s_step = Guid.Parse("0193a4b2-0000-7000-8000-00000000c001");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ddt-shares-{Guid.NewGuid():N}");
    private readonly FakeMounts _mounts = new();
    private readonly StringWriter _console = new();
    private readonly AgentLog _log;

    public LinuxShareConnectorTests() => _log = new AgentLog(new ImmediateTimeProvider(), _console) { StepId = s_step };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        _console.Dispose();
    }

    private string Place(int number) => Path.Combine(_directory, s_step.ToString("D"), $"{number}");

    private LinuxShareConnector Connector() => new(_mounts, _directory, _log)
    {
        Resolve = (host, _) => host == "files.example.test"
            ? Task.FromResult<IPAddress[]>([IPAddress.Parse("fd00::17"), IPAddress.Parse("10.0.2.2")])
            : Task.FromException<IPAddress[]>(new SocketException((int)SocketError.HostNotFound)),
        Kernel = () => "6.8.0-139-generic",
    };

    private Task<IConnectedShares> ConnectAsync(params AgentShareConnection[] shares) =>
        Connector().ConnectAsync(shares, null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task MountsEachShareAtAPlaceOfItsOwnAndTakesThemAwayWhenTheStepEnds()
    {
        IConnectedShares shares = await ConnectAsync(
            new AgentShareConnection(@"\\files.example.test\drivers\linux", @"CORP\ddt-read", Password),
            new AgentShareConnection(@"\\10.0.100.200\tools", "ddt", "second"));

        Assert.Equal([Place(1), Place(2)], shares.LocalPaths);
        Assert.Equal(
            [$"mount cifs //files.example.test/drivers/linux at {Place(1)}", $"mount cifs //10.0.100.200/tools at {Place(2)}"],
            _mounts.Calls);

        // The name is resolved here, and an address the kernel reaches by IPv4 comes first.
        Assert.Equal(
            [
                @"ip=10.0.2.2,username=ddt-read,domain=CORP,password=Pa55,,w0rd\ with,,,,commas,sec=ntlmssp,file_mode=0700,dir_mode=0700",
                "ip=10.0.100.200,username=ddt,password=second,sec=ntlmssp,file_mode=0700,dir_mode=0700",
            ],
            _mounts.Options);
        Assert.True(Directory.Exists(Place(2)));

        await shares.DisposeAsync();
        await shares.DisposeAsync();

        Assert.Equal([$"unmount {Place(2)} detached", $"unmount {Place(1)} detached"], _mounts.Calls.Skip(2));
        Assert.False(Directory.Exists(Path.Combine(_directory, s_step.ToString("D"))));
        Assert.Contains($@"Connected \\files.example.test\drivers\linux as CORP\ddt-read at {Place(1)}.", _console.ToString(), StringComparison.Ordinal);
        Assert.Contains(@"Disconnected \\10.0.100.200\tools.", _console.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Pa55", _console.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("password", _console.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // The errors two cloud images gave a Samba server, each with the sentence the step fails with.
    [Theory]
    [InlineData(19, "This Linux's kernel 6.8.0-139-generic has no CIFS file system, so \\\\10.0.2.2\\drivers cannot be connected for this step. " +
        "The kernel modules package of the image lacks it. Add it to the image or install it in an earlier step, or leave the share out.")]
    [InlineData(13, "\\\\10.0.2.2\\drivers could not be connected as ddt: the server did not take the account's name or password (error 13).")]
    [InlineData(2, "\\\\10.0.2.2\\drivers could not be connected: 10.0.2.2 has no such share, or the share no such folder (error 2).")]
    [InlineData(113, "\\\\10.0.2.2\\drivers could not be connected: 10.0.2.2 (10.0.2.2) cannot be reached from this machine (error 113).")]
    [InlineData(111, "\\\\10.0.2.2\\drivers could not be connected: 10.0.2.2 (10.0.2.2) takes no SMB connection on port 445 (error 111).")]
    [InlineData(22, "\\\\10.0.2.2\\drivers could not be connected as ddt (error 22). The kernel's log says why: dmesg shows it on the machine.")]
    public async Task SaysByTheKernelsErrorWhyAShareCouldNotBeConnected(int error, string message)
    {
        _mounts.Errors["//10.0.2.2/drivers"] = error;

        DeploymentStepException failure = await Assert.ThrowsAsync<DeploymentStepException>(
            () => ConnectAsync(new AgentShareConnection(@"\\10.0.2.2\tools", "ddt", "first"), new AgentShareConnection(@"\\10.0.2.2\drivers", "ddt", Password)));

        Assert.Equal(message, failure.Message);
        Assert.DoesNotContain("Pa55", failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Pa55", _console.ToString(), StringComparison.Ordinal);

        // The share that was connected before it is taken away again, and no folder stays.
        Assert.Equal($"unmount {Place(1)} detached", _mounts.Calls[^1]);
        Assert.False(Directory.Exists(Path.Combine(_directory, s_step.ToString("D"))));
    }

    [Fact]
    public async Task SaysThatAHostsNameCouldNotBeLookedUpAndMountsNothing()
    {
        DeploymentStepException failure = await Assert.ThrowsAsync<DeploymentStepException>(
            () => ConnectAsync(new AgentShareConnection(@"\\nowhere.example.test\drivers", "ddt", Password)));

        Assert.StartsWith(@"The name nowhere.example.test of \\nowhere.example.test\drivers could not be looked up (", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_mounts.Calls);
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task RefusesAnAccountTheMountCallCannotCarryWithoutNamingItsPassword()
    {
        DeploymentStepException failure = await Assert.ThrowsAsync<DeploymentStepException>(
            () => ConnectAsync(new AgentShareConnection(@"\\10.0.2.2\drivers", "ddt", "secret\0rest")));

        Assert.Equal(@"The account for \\10.0.2.2\drivers is too long for the kernel's mount call, or holds a character it cannot carry.", failure.Message);
        Assert.Empty(_mounts.Calls);
    }

    [Fact]
    public async Task AStoppedStepConnectsNoFurtherShareAndDisconnectsTheOnesItHas()
    {
        using CancellationTokenSource stop = new();
        _mounts.Mounted = (_, _) => stop.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Connector().ConnectAsync(
            [new AgentShareConnection(@"\\10.0.2.2\one", "ddt", "x"), new AgentShareConnection(@"\\10.0.2.2\two", "ddt", "x")],
            null,
            stop.Token));

        Assert.Equal([$"mount cifs //10.0.2.2/one at {Place(1)}", $"unmount {Place(1)} detached"], _mounts.Calls);
    }

    // An agent that was killed in a step left its shares mounted, and its next start has no step to end.
    [Fact]
    public async Task DisconnectsWhatAnEarlierStartLeftMounted()
    {
        await ConnectAsync(new AgentShareConnection(@"\\10.0.2.2\one", "ddt", "x"));
        string stale = Path.Combine(_directory, "another step", "1");
        Directory.CreateDirectory(stale);

        Connector().DisconnectLeftovers();

        Assert.Equal($"unmount {Place(1)} detached", _mounts.Calls[^1]);
        Assert.Empty(Directory.GetDirectories(_directory));
        Assert.Contains($"Disconnected a share an earlier start of the agent left at {Place(1)}.", _console.ToString(), StringComparison.Ordinal);
    }

    // A share whose unmount fails keeps its place, so nothing can delete through it what the share holds.
    [Fact]
    public async Task LeavesThePlaceOfAShareThatCouldNotBeDisconnected()
    {
        IConnectedShares shares = await ConnectAsync(new AgentShareConnection(@"\\10.0.2.2\one", "ddt", "x"));
        File.WriteAllText(Path.Combine(Place(1), "a file of the share"), "kept");
        _mounts.Unmount(Place(1), detach: true);

        await shares.DisposeAsync();

        Assert.True(File.Exists(Path.Combine(Place(1), "a file of the share")));
        Assert.Contains($@"\\10.0.2.2\one could not be disconnected from {Place(1)} (error 22).", _console.ToString(), StringComparison.Ordinal);
    }
}

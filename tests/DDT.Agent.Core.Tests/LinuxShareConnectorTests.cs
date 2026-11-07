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

    // What each name resolves to. A name that is not here cannot be found.
    private static readonly Dictionary<string, string[]> s_names = new(StringComparer.Ordinal)
    {
        ["files.example.test"] = ["fd00::17", "10.0.2.2"],
        ["three.example.test"] = ["fd00::17", "10.0.2.2", "10.0.2.3"],
        ["six.example.test"] = ["2001:db8::10"],
        ["link.example.test"] = ["fe80::1%3"],
        ["mapped.example.test"] = ["::ffff:10.0.2.9"],
        ["none.example.test"] = [],
    };

    private LinuxShareConnector Connector() => new(_mounts, _directory, _log)
    {
        Resolve = (host, _) => s_names.TryGetValue(host, out string[]? addresses)
            ? Task.FromResult<IPAddress[]>([.. addresses.Select(IPAddress.Parse)])
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
    public async Task SaysThatAHostsNameHasNoAddressAndMountsNothing()
    {
        DeploymentStepException failure = await Assert.ThrowsAsync<DeploymentStepException>(
            () => ConnectAsync(new AgentShareConnection(@"\\none.example.test\drivers", "ddt", Password)));

        Assert.Equal(@"The name none.example.test of \\none.example.test\drivers has no address, so the share cannot be connected.", failure.Message);
        Assert.Empty(_mounts.Calls);
    }

    // The kernel reads an IPv6 address without brackets, and a link's number after a percent sign.
    [Theory]
    [InlineData("six.example.test", "ip=2001:db8::10,")]
    [InlineData("link.example.test", "ip=fe80::1%3,")]
    [InlineData("mapped.example.test", "ip=10.0.2.9,")]
    [InlineData("fd00::99", "ip=fd00::99,")]
    public async Task GivesTheKernelAnIPv6AddressAsItReadsOne(string host, string option)
    {
        await using IConnectedShares shares = await ConnectAsync(new AgentShareConnection($@"\\{host}\drivers", "ddt", "x"));

        Assert.StartsWith(option, Assert.Single(_mounts.Options), StringComparison.Ordinal);
        Assert.Equal($"mount cifs //{host}/drivers at {Place(1)}", _mounts.Calls[0]);
    }

    // A server with several addresses, of which the machine reaches only the last.
    [Fact]
    public async Task TriesAHostsNextAddressWhenOneDoesNotAnswer()
    {
        _mounts.AddressErrors["10.0.2.2"] = 113;
        _mounts.AddressErrors["10.0.2.3"] = 111;

        await using IConnectedShares shares = await ConnectAsync(new AgentShareConnection(@"\\three.example.test\drivers", "ddt", Password));

        Assert.Equal(["ip=10.0.2.2,", "ip=10.0.2.3,", "ip=fd00::17,"], _mounts.Options.Select(options => options[..(options.IndexOf(',', StringComparison.Ordinal) + 1)]));
        Assert.Equal([Place(1)], shares.LocalPaths);
        Assert.Contains("three.example.test does not answer at 10.0.2.2 (error 113), so 10.0.2.3 is tried.", _console.ToString(), StringComparison.Ordinal);
        Assert.Contains("three.example.test does not answer at 10.0.2.3 (error 111), so fd00::17 is tried.", _console.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Pa55", _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NamesEveryAddressItTriedWhenTheHostAnswersAtNone()
    {
        _mounts.Errors["//three.example.test/drivers"] = 113;

        DeploymentStepException failure = await Assert.ThrowsAsync<DeploymentStepException>(
            () => ConnectAsync(new AgentShareConnection(@"\\three.example.test\drivers", "ddt", Password)));

        Assert.Equal(
            @"\\three.example.test\drivers could not be connected: three.example.test (10.0.2.2, 10.0.2.3, fd00::17) cannot be reached from this machine (error 113).",
            failure.Message);
        Assert.Equal(3, _mounts.Options.Count);
    }

    // The server took the connection and refused the account. Another of its addresses would only lock the account.
    [Fact]
    public async Task TriesNoOtherAddressOnceTheServerHasAnswered()
    {
        _mounts.Errors["//three.example.test/drivers"] = 13;

        await Assert.ThrowsAsync<DeploymentStepException>(() => ConnectAsync(new AgentShareConnection(@"\\three.example.test\drivers", "ddt", Password)));

        Assert.Single(_mounts.Options);
    }

    // A Linux with SELinux splits the options at every comma and keeps these words for itself. What follows one is
    // then in the kernel's log, so no mount is tried, and the sentence names the part and never what it holds.
    [Theory]
    [InlineData("ddt", "Pa55,context=system_u:object_r:x", "password")]
    [InlineData("ddt", "Pa55,,fscontext=x", "password")]
    [InlineData("ddt", "Pa55,seclabel", "password")]
    [InlineData(@"CORP\dd,defcontext=t", "Pa55", "user name")]
    [InlineData(@"CO,rootcontext=RP\ddt", "Pa55", "domain")]
    public async Task RefusesAnAccountAKernelWithSeLinuxWouldReadAnOptionOutOf(string userName, string password, string part)
    {
        DeploymentStepException failure = await Assert.ThrowsAsync<DeploymentStepException>(
            () => ConnectAsync(new AgentShareConnection(@"\\10.0.2.2\drivers", userName, password)));

        Assert.StartsWith($@"The {part} of the account for \\10.0.2.2\drivers holds a comma followed by context=, fscontext=,", failure.Message, StringComparison.Ordinal);
        Assert.EndsWith("so the share is not connected. Use an account without it.", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Pa55", failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("system_u", failure.ToString(), StringComparison.Ordinal);
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
        _mounts.UnmountErrors[Place(1)] = 16;

        await shares.DisposeAsync();

        Assert.True(File.Exists(Path.Combine(Place(1), "a file of the share")));
        Assert.Contains(
            $@"WARN  \\10.0.2.2\one could not be disconnected from {Place(1)} (error 16). It stays mounted until Linux restarts.",
            _console.ToString(),
            StringComparison.Ordinal);
    }

    // A script that ends with umount "$DDT_SHARE_1" has done what the agent was about to do.
    [Fact]
    public async Task AShareItsScriptDisconnectedIsNoFailureAndItsPlaceGoes()
    {
        IConnectedShares shares = await ConnectAsync(new AgentShareConnection(@"\\10.0.2.2\one", "ddt", "x"), new AgentShareConnection(@"\\10.0.2.2\two", "ddt", "x"));
        _mounts.Unmount(Place(1), detach: false);
        _mounts.Unmount(Place(2), detach: false);
        Directory.Delete(Place(2));

        await shares.DisposeAsync();

        Assert.False(Directory.Exists(Path.Combine(_directory, s_step.ToString("D"))));
        Assert.Contains(@"INFO  \\10.0.2.2\one was no longer connected when the step ended.", _console.ToString(), StringComparison.Ordinal);
        Assert.Contains(@"INFO  \\10.0.2.2\two was no longer connected when the step ended.", _console.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("WARN", _console.ToString(), StringComparison.Ordinal);
    }
}

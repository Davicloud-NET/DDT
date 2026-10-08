// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Facts;
using Xunit;

namespace DDT.Agent.Tests;

// A machine on which Linux shows no network adapter: the firmware started it from the network with a driver of its
// own, and Linux has none for the card, or the adapter is on USB and comes late.
public sealed class LinuxAdaptersTests : IDisposable
{
    private readonly LinuxTree _tree = new();
    private readonly StringWriter _console = new();

    public LinuxAdaptersTests() => Adapter("lo", "772", "00:00:00:00:00:00");

    public void Dispose() => _tree.Dispose();

    private void Adapter(string name, string type, string address)
    {
        _tree.Write(type + "\n", "sys", "class", "net", name, "type");
        _tree.Write(address + "\n", "sys", "class", "net", name, "address");
    }

    private AgentLog Log() => new(new ImmediateTimeProvider(), _console);

    [Fact]
    public void NamesTheAdaptersWithAnAddressOfTheirOwn()
    {
        Adapter("eth1", "1", "52:54:00:dd:70:02");
        Adapter("eth0", "1", "52:54:00:dd:70:01");
        Adapter("sit0", "776", "00:00:00:00");
        Adapter("bonding_masters", "1", "00:00:00:00:00:00");

        Assert.Equal(["eth0", "eth1"], new LinuxAdapters(_tree.Root).Names());
        Assert.Empty(new LinuxAdapters(_tree.PathOf("nowhere")).Names());
    }

    [Fact]
    public async Task GoesOnAtOnceAndSaysNothingWhereAnAdapterIsThere()
    {
        Adapter("eth0", "1", "52:54:00:dd:70:01");

        Assert.True(await new LinuxAdapters(_tree.Root).WaitAsync(Log(), new ManualTimeProvider(), TestContext.Current.CancellationToken));
        Assert.Equal("", _console.ToString());
    }

    [Fact]
    public async Task SaysOnceWhatIsWrongAndWhatToDoAndGoesOnWhenAnAdapterAppears()
    {
        ManualTimeProvider time = new();
        Task<bool> waiting = new LinuxAdapters(_tree.Root).WaitAsync(Log(), time, TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromSeconds(2));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(waiting.IsCompleted);

        Adapter("eth0", "1", "00:e0:4c:68:00:01");
        await time.AdvanceUntilAsync(TimeSpan.FromSeconds(2), () => waiting.IsCompleted);

        Assert.True(await waiting);
        string[] lines = _console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith(
            "WARN  Linux shows no network adapter on this machine, so the agent cannot reach the server. The firmware started the machine " +
            "from the network with a driver of its own, but the Linux boot environment has no driver or no firmware for this network " +
            "card. Connect a USB network adapter or a dock with one: the agent goes on as soon as an adapter appears.",
            lines[0],
            StringComparison.Ordinal);
        Assert.EndsWith("INFO  Linux shows a network adapter now, eth0. The agent goes on.", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopsWaitingWhenTheAgentIsStopped()
    {
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<bool> waiting = new LinuxAdapters(_tree.Root).WaitAsync(Log(), new ManualTimeProvider(), stop.Token);

        await stop.CancelAsync();

        Assert.False(await waiting);
    }

    // The reader both systems share throws what ends the agent. Here the registration gets a sentence to show and waits.
    [Fact]
    public void WithoutAnAdapterTheMachinesIdentitySaysSoInAWayTheRegistrationWaitsFor()
    {
        MachineIdentity identity = new DryRunMachineIdentityReader(1).Read();
        LinuxIdentityReader reader = new(new SequenceIdentityReader(identity), new LinuxAdapters(_tree.Root));

        Assert.Equal(LinuxAdapters.NoneMessage, Assert.Throws<NoNetworkAdapterException>(() => reader.Read()).Message);

        Adapter("eth0", "1", "52:54:00:dd:70:01");

        Assert.Same(identity, reader.Read());
    }
}

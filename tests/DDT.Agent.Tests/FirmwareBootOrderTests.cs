// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class FirmwareBootOrderTests
{
    private static readonly EspPartition s_esp = new(1, 2048, 614400, Guid.Parse("5e2b1a3c-4d6f-4a8b-9c0d-1e2f3a4b5c6d"));

    private readonly FakeUefiVariables _variables = new();
    private readonly AgentLog _log = new(new ImmediateTimeProvider(), TextWriter.Null);

    [Fact]
    public async Task PutsTheLoaderFirstAndTheOrderBackAsItWas()
    {
        byte[] network = Order(0);
        _variables.Values["Boot0000"] = EfiLoadOption.Build("EFI Network", s_esp with { PartitionId = Guid.NewGuid() }, @"\EFI\Microsoft\Boot\bootmgfw.efi");
        _variables.Values["BootOrder"] = network;
        FirmwareBootOrder order = new(_variables, _log);

        await order.PutFirstAsync(s_esp, FirmwareBootEntry.FallbackLoaderPath, "Ubuntu", [], TestContext.Current.CancellationToken);

        Assert.Equal(Order(1, 0), _variables.Values["BootOrder"]);
        Assert.Equal(["Boot0001", "BootOrder"], _variables.Writes);

        await order.RestoreAsync(TestContext.Current.CancellationToken);

        Assert.Equal(network, _variables.Values["BootOrder"]);
        Assert.Equal(["Boot0001"], _variables.Deletes);
        Assert.Contains(await SentAsync(), line => line.Level == AgentLogLevel.Information && line.Message.StartsWith(
            "The firmware boot order is back as it was before the deployment",
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnlyWarnsWhenTheFirmwareRefuses()
    {
        _variables.Failure = new DeploymentStepException("The firmware refused.");
        FirmwareBootOrder order = new(_variables, _log);

        await order.PutFirstAsync(s_esp, FirmwareBootEntry.FallbackLoaderPath, "Ubuntu", [], TestContext.Current.CancellationToken);
        await order.RestoreAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_variables.Writes);
        Assert.Contains(await SentAsync(), line => line.Level == AgentLogLevel.Warning && line.Message ==
            "Ubuntu could not be put first in the firmware boot order (The firmware refused.), so this machine may start from the " +
            "network again. Set its boot order to start the disk first.");
    }

    [Fact]
    public async Task PutsNothingBackThatAnEarlierRunChanged()
    {
        _variables.Values["BootOrder"] = Order(0);
        FirmwareBootOrder order = new(_variables, _log);
        await order.PutFirstAsync(s_esp, FirmwareBootEntry.FallbackLoaderPath, "Ubuntu", [], TestContext.Current.CancellationToken);
        _variables.Writes.Clear();

        order.Forget();
        await order.RestoreAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_variables.Writes);
        Assert.Empty(_variables.Deletes);
    }

    [Fact]
    public async Task ChangesNothingWhenThePartitionCannotBeFound()
    {
        _variables.Values["BootOrder"] = Order(0);
        FirmwareBootOrder order = new(_variables, _log);

        Assert.Throws<DeploymentStepException>(() => order.MakeFirst(
            () => throw new DeploymentStepException("The partition cannot be read."),
            FirmwareBootEntry.WindowsLoaderPath,
            FirmwareBootEntry.WindowsDescription,
            []));
        await order.RestoreAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_variables.Writes);
        Assert.Empty(_variables.Deletes);
    }

    private static byte[] Order(params ushort[] numbers)
    {
        byte[] order = new byte[numbers.Length * 2];

        for (int index = 0; index < numbers.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(order.AsSpan(index * 2), numbers[index]);
        }

        return order;
    }

    private async Task<List<AgentLogLine>> SentAsync()
    {
        ScriptedAgentServer server = new();

        while (_log.QueuedLines > 0)
        {
            await _log.FlushAsync(server, Guid.Empty, "token", TestContext.Current.CancellationToken);
        }

        return server.SentLines;
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// The boot order through efivarfs, and the list of the firmware's entries that goes into the machine log.
public sealed class LinuxBootOrderTests : IDisposable
{
    private static readonly EspPartition s_esp = new(15, 262144, 253952, Guid.Parse("5e2b1a3c-4d6f-4a8b-9c0d-1e2f3a4b5c6d"));

    private readonly LinuxTree _tree = new();
    private readonly StringWriter _console = new();
    private readonly LinuxUefiVariables _variables;

    public LinuxBootOrderTests()
    {
        Directory.CreateDirectory(_tree.Variables);
        _variables = _tree.Firmware();
    }

    public void Dispose() => _tree.Dispose();

    // What OVMF makes for a network card: the description, then a MAC address node, an IPv4 node and the end node.
    private static byte[] NetworkEntry(uint attributes = 1)
    {
        byte[] description = Encoding.Unicode.GetBytes("UEFI PXEv4 (MAC:525400DD7001)\0");
        byte[] mac = [0x03, 0x0B, 37, 0, 0x52, 0x54, 0x00, 0xDD, 0x70, 0x01, .. new byte[26], 1];
        byte[] ipv4 = [0x03, 0x0C, 27, 0, .. new byte[23]];
        byte[] end = [0x7F, 0xFF, 4, 0];

        return [.. BitConverter.GetBytes(attributes), .. BitConverter.GetBytes((ushort)(mac.Length + ipv4.Length + end.Length)), .. description, .. mac, .. ipv4, .. end];
    }

    private void FirmwareOfAMachineThatStartedFromTheNetwork()
    {
        _tree.WriteVariable("Boot0000", LinuxUefiVariables.Global, [1, 0, 0, 0, 0, 0, .. Encoding.Unicode.GetBytes("UiApp\0")]);
        _tree.WriteVariable("Boot0001", LinuxUefiVariables.Global, NetworkEntry());
        _tree.WriteVariable("BootOrder", LinuxUefiVariables.Global, 0x00, 0x00, 0x01, 0x00);
        _tree.WriteVariable("BootCurrent", LinuxUefiVariables.Global, 0x01, 0x00);
    }

    [Fact]
    public async Task PutsTheDiskFirstAndListsTheFirmwaresEntriesInTheLog()
    {
        FirmwareOfAMachineThatStartedFromTheNetwork();
        LinuxBootOrder order = new(_variables, new AgentLog(new ImmediateTimeProvider(), _console));

        await order.PutFirstAsync(s_esp, FirmwareBootEntry.FallbackLoaderPath, "debian-13", [], TestContext.Current.CancellationToken);

        Assert.Equal([0x02, 0x00, 0x00, 0x00, 0x01, 0x00], _variables.Read("BootOrder"));
        string console = _console.ToString();
        Assert.Contains("Added firmware boot entry Boot0002 for debian-13", console, StringComparison.Ordinal);
        Assert.Contains("The firmware's boot order: Boot0002, Boot0000, Boot0001.", console, StringComparison.Ordinal);
        Assert.Contains(@"  Boot0002: debian-13, partition 15 (5e2b1a3c-4d6f-4a8b-9c0d-1e2f3a4b5c6d) \EFI\BOOT\BOOTX64.EFI", console, StringComparison.Ordinal);
        Assert.Contains("  Boot0000: UiApp", console, StringComparison.Ordinal);
        Assert.Contains("  Boot0001: UEFI PXEv4 (MAC:525400DD7001), network adapter 525400DD7001 over IPv4", console, StringComparison.Ordinal);
        Assert.Contains("  This start came from Boot0001, and the next one comes from the boot order.", console, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PutsTheOrderBackForARunThatDidNotFinishAndSaysWhatThatPrevents()
    {
        FirmwareOfAMachineThatStartedFromTheNetwork();
        LinuxBootOrder order = new(_variables, new AgentLog(new ImmediateTimeProvider(), _console));
        await order.PutFirstAsync(s_esp, FirmwareBootEntry.FallbackLoaderPath, "debian-13", [], TestContext.Current.CancellationToken);

        await order.RestoreAsync(TestContext.Current.CancellationToken);

        Assert.Equal([0x00, 0x00, 0x01, 0x00], _variables.Read("BootOrder"));
        Assert.Null(_variables.Read("Boot0002"));
        Assert.Contains("so this machine does not start a disk image whose run did not finish.", _console.ToString(), StringComparison.Ordinal);
        Assert.EndsWith("The firmware's boot order: Boot0000, Boot0001.", _console.ToString().Split(Environment.NewLine).Last(line => line.Contains("boot order:", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    // The run before left its own entry, and the image it wrote added one at its first start. The disk is erased, so
    // neither leads anywhere: one is used again, the other goes.
    [Fact]
    public async Task ASecondEntryForTheErasedDiskIsRemovedAndTheLogSaysSo()
    {
        Guid erased = Guid.Parse("7a6b5c4d-3e2f-4a1b-8c9d-0e1f2a3b4c5d");
        EspPartition old = s_esp with { PartitionId = erased };
        FirmwareOfAMachineThatStartedFromTheNetwork();
        _tree.WriteVariable("Boot0002", LinuxUefiVariables.Global, EfiLoadOption.Build("debian-12", old, FirmwareBootEntry.FallbackLoaderPath));
        _tree.WriteVariable("Boot0003", LinuxUefiVariables.Global, EfiLoadOption.Build("debian", old, @"\EFI\debian\shimx64.efi"));
        _tree.WriteVariable("BootOrder", LinuxUefiVariables.Global, 0x03, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01, 0x00);
        LinuxBootOrder order = new(_variables, new AgentLog(new ImmediateTimeProvider(), _console));

        await order.PutFirstAsync(s_esp, FirmwareBootEntry.FallbackLoaderPath, "debian-13", [erased], TestContext.Current.CancellationToken);

        Assert.Equal([0x03, 0x00, 0x00, 0x00, 0x01, 0x00], _variables.Read("BootOrder"));
        Assert.Equal(EfiLoadOption.Build("debian-13", s_esp, FirmwareBootEntry.FallbackLoaderPath), _variables.Read("Boot0003"));
        Assert.Null(_variables.Read("Boot0002"));
        string console = _console.ToString();
        Assert.Contains("Firmware boot entry Boot0003 pointed at the EFI system partition this deployment erased.", console, StringComparison.Ordinal);
        Assert.Contains("Removed firmware boot entry Boot0002, which pointed at an EFI system partition this deployment erased.", console, StringComparison.Ordinal);
        Assert.Contains("The firmware's boot order: Boot0003, Boot0000, Boot0001.", console, StringComparison.Ordinal);

        // A run that fails after this gets all of it back.
        await order.RestoreAsync(TestContext.Current.CancellationToken);
        Assert.Equal([0x03, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01, 0x00], _variables.Read("BootOrder"));
        Assert.Equal(EfiLoadOption.Build("debian-12", old, FirmwareBootEntry.FallbackLoaderPath), _variables.Read("Boot0002"));
    }

    // What other systems and firmware wrote can be anything. The list never fails the run for it.
    [Fact]
    public void DescribesWhatItCanOfEntriesItDoesNotKnow()
    {
        _tree.WriteVariable("BootOrder", LinuxUefiVariables.Global, 0x03, 0x00, 0x07, 0x00, 0x09);
        _tree.WriteVariable("Boot0003", LinuxUefiVariables.Global, NetworkEntry(attributes: 0));
        _tree.WriteVariable("BootNext", LinuxUefiVariables.Global, 0x03, 0x00);

        IReadOnlyList<string> lines = FirmwareBootEntries.Describe(_variables);

        Assert.Equal(
            [
                "The firmware's boot order: Boot0003, Boot0007.",
                "  Boot0003: UEFI PXEv4 (MAC:525400DD7001), network adapter 525400DD7001 over IPv4, switched off",
                "  Boot0007: no such entry",
                "  This start came from an entry the firmware does not name, and the next one comes from Boot0003.",
            ],
            lines);
        Assert.Equal("not a boot entry", FirmwareBootEntries.Describe([1, 2, 3]));
        Assert.Equal("(no description)", FirmwareBootEntries.Describe([1, 0, 0, 0, 200, 0, 0, 0, 4, 1, 99]));
        Assert.Equal("Windows Boot Manager", FirmwareBootEntries.Describe([1, 0, 0, 0, 0, 0, .. Encoding.Unicode.GetBytes("Windows Boot Manager")]));
    }

    [Fact]
    public void SaysThatAFirmwareWithoutABootOrderHasNone()
    {
        Assert.Equal("The firmware has no boot order.", FirmwareBootEntries.Describe(_variables)[0]);
    }
}

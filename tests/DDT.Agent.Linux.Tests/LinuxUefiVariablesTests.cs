// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

// The firmware's variables as files of efivarfs, and the bytes the agent's own writers leave there.
public sealed class LinuxUefiVariablesTests : IDisposable
{
    private static readonly EspPartition s_esp = new(15, 262144, 253952, Guid.Parse("5e2b1a3c-4d6f-4a8b-9c0d-1e2f3a4b5c6d"));

    private readonly LinuxTree _tree = new();
    private readonly List<string> _madeMutable = [];
    private readonly AgentLog _log = new(new ImmediateTimeProvider(), TextWriter.Null);
    private readonly LinuxUefiVariables _variables;

    public LinuxUefiVariablesTests()
    {
        Directory.CreateDirectory(_tree.Variables);
        _variables = _tree.Firmware(path => _madeMutable.Add(Path.GetFileName(path)));
    }

    public void Dispose() => _tree.Dispose();

    [Fact]
    public void ReadsAVariableWithoutItsAttributes()
    {
        _tree.WriteVariable("BootCurrent", LinuxUefiVariables.Global, 0x03, 0x00);
        _tree.WriteVariable("Empty", LinuxUefiVariables.Global);

        Assert.Equal([0x03, 0x00], _variables.Read("BootCurrent"));
        Assert.Equal(Array.Empty<byte>(), _variables.Read("Empty"));
        Assert.Null(_variables.Read("BootNext"));
        Assert.Null(_variables.Read("BootCurrent", ResumeCarrier.Vendor));
    }

    [Fact]
    public void NamesAFileAsTheKernelDoes()
    {
        Assert.Equal("BootOrder-8be4df61-93ca-11d2-aa0d-00e098032b8c", LinuxUefiVariables.FileName("BootOrder", LinuxUefiVariables.Global));
        Assert.Equal("DdtResume-d34b9a64-3d20-47de-94c2-e70dfc2ecce0", LinuxUefiVariables.FileName(ResumeCarrier.VariableName, ResumeCarrier.Vendor));
        Assert.Throws<ArgumentException>(() => _variables.Read("../BootOrder"));
        Assert.Throws<ArgumentException>(() => _variables.Write("a/b", [1]));
    }

    // Non-volatile, boot service and runtime access: 07 00 00 00, then the value, as one file content.
    [Fact]
    public void WritesTheAttributesBeforeTheValue()
    {
        _variables.Write("BootNext", [0x03, 0x00]);

        Assert.Equal([0x07, 0x00, 0x00, 0x00, 0x03, 0x00], _tree.ReadVariableFile("BootNext", LinuxUefiVariables.Global));
        Assert.Equal([0x03, 0x00], _variables.Read("BootNext"));
    }

    // A restart back into the boot environment: BootNext becomes BootCurrent.
    [Fact]
    public void TheNextStartIsWrittenAsTheEntryThisStartCameFrom()
    {
        _tree.WriteVariable("BootCurrent", LinuxUefiVariables.Global, 0x02, 0x00);
        _tree.WriteVariable("Boot0002", LinuxUefiVariables.Global, 1, 0, 0, 0, 0, 0, (byte)'P', 0, 0, 0);

        new NextStart(_variables, _log, AgentEnvironment.LinuxBoot).FromThisEntry();

        Assert.Equal([0x07, 0x00, 0x00, 0x00, 0x02, 0x00], _tree.ReadVariableFile("BootNext", LinuxUefiVariables.Global));
    }

    // The disk first: a new Boot#### with the lowest free number, then BootOrder with it in front.
    [Fact]
    public void ANewBootEntryAndTheOrderAreWrittenAsTheFirmwareReadsThem()
    {
        _tree.WriteVariable("Boot0000", LinuxUefiVariables.Global, 1, 0, 0, 0, 0, 0, (byte)'U', 0, 0, 0);
        _tree.WriteVariable("Boot0001", LinuxUefiVariables.Global, 1, 0, 0, 0, 0, 0, (byte)'N', 0, 0, 0);
        _tree.WriteVariable("BootOrder", LinuxUefiVariables.Global, 0x00, 0x00, 0x01, 0x00);

        new FirmwareBootEntry(_variables, _log).MakeFirst(s_esp, FirmwareBootEntry.FallbackLoaderPath, "debian-13", []);

        byte[] entry = _tree.ReadVariableFile("Boot0002", LinuxUefiVariables.Global);
        Assert.Equal([0x07, 0x00, 0x00, 0x00, .. EfiLoadOption.Build("debian-13", s_esp, FirmwareBootEntry.FallbackLoaderPath)], entry);
        Assert.True(EfiLoadOption.PointsAt(entry.AsSpan(LinuxUefiVariables.AttributesLength), s_esp.PartitionId, @"\EFI\BOOT\BOOTX64.EFI"));
        Assert.Equal([0x07, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01, 0x00], _tree.ReadVariableFile("BootOrder", LinuxUefiVariables.Global));

        // BootOrder was there, so the kernel is asked to let it be changed. The new entry was not.
        Assert.Equal(["BootOrder-8be4df61-93ca-11d2-aa0d-00e098032b8c"], _madeMutable);
    }

    // Version 1, target 2 for the Linux boot environment, then the token in ASCII.
    [Fact]
    public void TheResumeTokenCrossesARestartInDdtsOwnVariable()
    {
        ResumeCarrier windowsPE = new(_variables, AgentEnvironment.WindowsPE, _log);

        Assert.True(windowsPE.Put(AgentEnvironment.LinuxBoot, "resume-1"));

        Assert.Equal(
            [0x07, 0x00, 0x00, 0x00, 0x01, 0x02, .. "resume-1"u8],
            _tree.ReadVariableFile(ResumeCarrier.VariableName, ResumeCarrier.Vendor));
        Assert.Empty(_madeMutable);

        ResumeCarrier linux = new(_variables, AgentEnvironment.LinuxBoot, _log);

        Assert.Equal("resume-1", linux.Take());
        Assert.False(linux.SwitchFailed);
        Assert.False(File.Exists(Path.Combine(_tree.Variables, "DdtResume-d34b9a64-3d20-47de-94c2-e70dfc2ecce0")));
        Assert.Null(linux.Take());
    }

    // The kernel makes a variable of an own GUID immutable when it is created, and refuses the next write and the
    // delete until the flag is cleared.
    [Fact]
    public void AVariableIsMadeMutableBeforeItIsWrittenAgainOrDeleted()
    {
        _variables.Write("DdtResume", ResumeCarrier.Vendor, [1]);
        Assert.Empty(_madeMutable);

        _variables.Write("DdtResume", ResumeCarrier.Vendor, [2, 3]);
        _variables.Delete("DdtResume", ResumeCarrier.Vendor);
        _variables.Delete("DdtResume", ResumeCarrier.Vendor);

        Assert.Equal(["DdtResume-d34b9a64-3d20-47de-94c2-e70dfc2ecce0", "DdtResume-d34b9a64-3d20-47de-94c2-e70dfc2ecce0"], _madeMutable);
        Assert.Null(_variables.Read("DdtResume", ResumeCarrier.Vendor));
    }

    // The kernel takes each write for the whole variable, so a value must go out in one, attributes and all. Nothing
    // may ask the file for its length or a place in it, which efivarfs does not have.
    [Fact]
    public void AVariableIsOneWriteAndAShorterValueReplacesALongerOne()
    {
        _variables.Write("BootOrder", [1, 0, 2, 0, 3, 0]);
        _variables.Write("BootOrder", [3, 0]);

        Assert.Equal([3, 0], _variables.Read("BootOrder"));
        Assert.Equal(2, _tree.Efivarfs.Writes[LinuxUefiVariables.FileName("BootOrder", LinuxUefiVariables.Global)]);
        Assert.Equal([0x07, 0x00, 0x00, 0x00, 0x03, 0x00], _tree.ReadVariableFile("BootOrder", LinuxUefiVariables.Global));
    }

    // Without clearing the flag the second write of a variable of DDT's own is refused, as on a machine.
    [Fact]
    public void TheKernelRefusesToChangeAVendorsVariableThatWasNotMadeMutable()
    {
        LinuxUefiVariables forgetful = new(_tree.Root, _ => { }, _tree.Efivarfs.Open);
        forgetful.Write("DdtResume", ResumeCarrier.Vendor, [1]);

        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => forgetful.Write("DdtResume", ResumeCarrier.Vendor, [2]));

        Assert.Equal("The firmware variable DdtResume cannot be written (Operation not permitted).", refusal.Message);
        Assert.Equal([1], forgetful.Read("DdtResume", ResumeCarrier.Vendor));

        // The boot variables of the specification stay mutable.
        forgetful.Write("BootNext", [1, 0]);
        forgetful.Write("BootNext", [2, 0]);
    }

    // The kernel checks a boot entry before the firmware gets it.
    [Fact]
    public void SaysWhenTheKernelRefusesWhatIsNoBootEntry()
    {
        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => _variables.Write("Boot0009", [1, 2, 3]));

        Assert.Equal("The firmware variable Boot0009 cannot be written (Invalid argument).", refusal.Message);
        Assert.Null(_variables.Read("Boot0009"));
    }

    [Fact]
    public void SaysThatTheMachineDidNotStartInUefiModeWhereThereAreNoVariables()
    {
        LinuxUefiVariables legacy = new(_tree.PathOf("legacy"), _ => { });

        DeploymentStepException read = Assert.Throws<DeploymentStepException>(() => legacy.Read("BootOrder"));
        DeploymentStepException write = Assert.Throws<DeploymentStepException>(() => legacy.Write("BootNext", [0, 0]));
        DeploymentStepException delete = Assert.Throws<DeploymentStepException>(() => legacy.Delete("BootNext", LinuxUefiVariables.Global));

        Assert.Equal("This machine did not start in UEFI mode, or efivarfs is not mounted, so its firmware variables cannot be read.", read.Message);
        Assert.Equal("This machine did not start in UEFI mode, or efivarfs is not mounted, so its firmware variables cannot be written.", write.Message);
        Assert.EndsWith("cannot be deleted.", delete.Message, StringComparison.Ordinal);
        Assert.False(legacy.HasVariables);
    }

    // The kernel answers a write the firmware has no room for with ENOSPC, which .NET hands on as error 28.
    [Fact]
    public void SaysThatTheVariableStoreIsFull()
    {
        _tree.WriteVariable("BootOrder", LinuxUefiVariables.Global, 0, 0);
        LinuxUefiVariables denied = new(_tree.Root, _ => throw new UnauthorizedAccessException("Operation not permitted"), _tree.Efivarfs.Open);
        _tree.Efivarfs.WriteFailure = new IOException("No space left on device", 28);

        DeploymentStepException refusal = Assert.Throws<DeploymentStepException>(() => _variables.Write("BootOrder", [1, 0]));
        DeploymentStepException other = Assert.Throws<DeploymentStepException>(() => denied.Write("BootOrder", [1, 0]));

        Assert.Equal([0, 0], _variables.Read("BootOrder"));

        Assert.Equal(
            "The firmware variable BootOrder cannot be written: the firmware's variable store is full. Delete boot entries nobody " +
            "needs in the firmware setup, or reset its settings there.",
            refusal.Message);
        Assert.Equal("The firmware variable BootOrder cannot be written (Operation not permitted).", other.Message);
    }

    // The carrier only warns when the firmware keeps nothing, and the machine then registers anew.
    [Fact]
    public void AFailedWriteCostsTheCarrierOnlyTheSignIn()
    {
        _tree.WriteVariable(ResumeCarrier.VariableName, ResumeCarrier.Vendor, 9);
        StringWriter console = new();
        _tree.Efivarfs.WriteFailure = new IOException("No space left on device", 28);
        ResumeCarrier carrier = new(_variables, AgentEnvironment.LinuxBoot, new AgentLog(new ImmediateTimeProvider(), console));

        Assert.False(carrier.Put(AgentEnvironment.WindowsPE, "resume-1"));
        Assert.Contains("the firmware's variable store is full", console.ToString(), StringComparison.Ordinal);
    }
}

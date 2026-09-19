using System.Buffers.Binary;
using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class WindowsBootEntryTests
{
    private static readonly EspPartition s_esp = new(1, 2048, 614400, Guid.Parse("5e2b1a3c-4d6f-4a8b-9c0d-1e2f3a4b5c6d"));

    // The EFI system partition this disk had before the deployment erased it.
    private static readonly EspPartition s_erasedEsp = s_esp with { PartitionId = Guid.Parse("7a6b5c4d-3e2f-4a1b-8c9d-0e1f2a3b4c5d") };

    // Another disk's EFI system partition, which the deployment left alone.
    private static readonly EspPartition s_otherEsp = s_esp with { PartitionId = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0") };

    private static readonly Guid[] s_erased = [s_erasedEsp.PartitionId];

    private readonly FakeUefiVariables _variables = new();
    private readonly AgentLog _log = new(new ImmediateTimeProvider(), TextWriter.Null);

    [Fact]
    public void MovesTheEntryForTheNewPartitionFirst()
    {
        _variables.Values["Boot0001"] = Other("EFI Network");
        _variables.Values["Boot0002"] = Other("EFI SCSI Device");
        _variables.Values["Boot0004"] = [.. Windows(s_esp), .. "WINDOWS\0"u8];
        _variables.Values["BootOrder"] = Order(1, 2, 4);

        WindowsBootEntry.MakeFirst(_variables, s_esp, s_erased, _log);

        Assert.Equal(Order(4, 1, 2), _variables.Values["BootOrder"]);
        Assert.Equal(["BootOrder"], _variables.Writes);
    }

    [Fact]
    public void AddsAnEntryUnderTheFirstFreeNumber()
    {
        _variables.Values["Boot0000"] = Other("EFI Network");
        _variables.Values["Boot0001"] = Other("FrontPage");
        _variables.Values["Boot0002"] = Other("EFI SCSI Device");
        _variables.Values["BootOrder"] = Order(0, 2);

        WindowsBootEntry.MakeFirst(_variables, s_esp, s_erased, _log);

        Assert.Equal(Windows(s_esp), _variables.Values["Boot0003"]);
        Assert.Equal(Order(3, 0, 2), _variables.Values["BootOrder"]);
        Assert.Equal(["Boot0003", "BootOrder"], _variables.Writes);
    }

    [Fact]
    public void PutsItsOwnEntryBeforeAWindowsEntryForAnotherDisk()
    {
        byte[] otherDisk = Windows(s_otherEsp);
        _variables.Values["Boot0000"] = otherDisk;
        _variables.Values["Boot0001"] = Other("EFI Network");
        _variables.Values["BootOrder"] = Order(0, 1);

        WindowsBootEntry.MakeFirst(_variables, s_esp, s_erased, _log);

        Assert.Equal(Windows(s_esp), _variables.Values["Boot0002"]);
        Assert.Equal(Order(2, 0, 1), _variables.Values["BootOrder"]);
        Assert.Same(otherDisk, _variables.Values["Boot0000"]);
    }

    [Fact]
    public void ReusesTheEntryForTheErasedPartition()
    {
        _variables.Values["Boot0000"] = Windows(s_erasedEsp);
        _variables.Values["Boot0001"] = Other("EFI Network");
        _variables.Values["BootOrder"] = Order(0, 1);

        WindowsBootEntry.MakeFirst(_variables, s_esp, s_erased, _log);

        Assert.Equal(Windows(s_esp), _variables.Values["Boot0000"]);
        Assert.False(_variables.Values.ContainsKey("Boot0002"));
        Assert.Equal(Order(0, 1), _variables.Values["BootOrder"]);
        Assert.Equal(["Boot0000"], _variables.Writes);
    }

    [Fact]
    public void MovesAReusedEntryFirst()
    {
        _variables.Values["Boot0001"] = Other("EFI Network");
        _variables.Values["Boot0003"] = Windows(s_erasedEsp);
        _variables.Values["BootOrder"] = Order(1, 3);

        WindowsBootEntry.MakeFirst(_variables, s_esp, s_erased, _log);

        Assert.Equal(Windows(s_esp), _variables.Values["Boot0003"]);
        Assert.Equal(Order(3, 1), _variables.Values["BootOrder"]);
        Assert.Equal(["Boot0003", "BootOrder"], _variables.Writes);
    }

    [Fact]
    public void PrefersTheEntryForTheNewPartitionToOneForTheErasedPartition()
    {
        byte[] erased = Windows(s_erasedEsp);
        _variables.Values["Boot0000"] = erased;
        _variables.Values["Boot0005"] = Windows(s_esp);
        _variables.Values["BootOrder"] = Order(0, 5);

        WindowsBootEntry.MakeFirst(_variables, s_esp, s_erased, _log);

        Assert.Same(erased, _variables.Values["Boot0000"]);
        Assert.Equal(Order(5, 0), _variables.Values["BootOrder"]);
        Assert.Equal(["BootOrder"], _variables.Writes);
    }

    [Fact]
    public void KeepsTheRestOfTheOrderOnceEach()
    {
        _variables.Values["Boot0001"] = Other("EFI Network");
        _variables.Values["Boot0002"] = Other("EFI SCSI Device");
        _variables.Values["Boot0005"] = Windows(s_esp);
        _variables.Values["BootOrder"] = Order(1, 5, 2, 1, 7);

        WindowsBootEntry.MakeFirst(_variables, s_esp, s_erased, _log);

        Assert.Equal(Order(5, 1, 2, 7), _variables.Values["BootOrder"]);
    }

    [Fact]
    public void StartsABootOrderWhenTheFirmwareHasNone()
    {
        WindowsBootEntry.MakeFirst(_variables, s_esp, s_erased, _log);

        Assert.Equal(Windows(s_esp), _variables.Values["Boot0000"]);
        Assert.Equal(Order(0), _variables.Values["BootOrder"]);
    }

    [Fact]
    public void WritesNothingWhenWindowsStartsFirstAlready()
    {
        _variables.Values["Boot0003"] = Windows(s_esp);
        _variables.Values["Boot0001"] = Other("EFI Network");
        _variables.Values["BootOrder"] = Order(3, 1);

        WindowsBootEntry.MakeFirst(_variables, s_esp, s_erased, _log);

        Assert.Empty(_variables.Writes);
    }

    [Fact]
    public void UndoPutsBackAnAddedEntryAndTheOrder()
    {
        _variables.Values["Boot0000"] = Other("EFI Network");
        _variables.Values["BootOrder"] = Order(0);
        Dictionary<string, byte[]> before = new(_variables.Values);
        UndoableUefiVariables changes = new(_variables);

        WindowsBootEntry.MakeFirst(changes, s_esp, s_erased, _log);

        Assert.True(changes.Undo());
        Assert.Equal(before, _variables.Values);
        Assert.Equal(["Boot0001"], _variables.Deletes);
        Assert.False(changes.Undo());
    }

    [Fact]
    public void UndoPutsBackAReusedEntry()
    {
        _variables.Values["Boot0000"] = Other("EFI Network");
        _variables.Values["Boot0001"] = Windows(s_erasedEsp);
        _variables.Values["BootOrder"] = Order(0, 1);
        Dictionary<string, byte[]> before = new(_variables.Values);
        UndoableUefiVariables changes = new(_variables);

        WindowsBootEntry.MakeFirst(changes, s_esp, s_erased, _log);

        Assert.Equal(Windows(s_esp), _variables.Values["Boot0001"]);
        Assert.True(changes.Undo());
        Assert.Equal(before, _variables.Values);
        Assert.Empty(_variables.Deletes);
    }

    [Fact]
    public void UndoDeletesABootOrderTheFirmwareDidNotHave()
    {
        UndoableUefiVariables changes = new(_variables);

        WindowsBootEntry.MakeFirst(changes, s_esp, s_erased, _log);

        Assert.True(changes.Undo());
        Assert.Empty(_variables.Values);
        Assert.Equal(["BootOrder", "Boot0000"], _variables.Deletes);
    }

    private static byte[] Windows(EspPartition esp) => EfiLoadOption.Build(WindowsBootEntry.Description, esp, WindowsBootEntry.LoaderPath);

    // Another disk's fallback loader, standing in for any entry that is not Windows Boot Manager on the new partition.
    private static byte[] Other(string description) => EfiLoadOption.Build(description, s_otherEsp, @"\EFI\Boot\bootx64.efi");

    private static byte[] Order(params ushort[] numbers)
    {
        byte[] order = new byte[numbers.Length * 2];

        for (int index = 0; index < numbers.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(order.AsSpan(index * 2), numbers[index]);
        }

        return order;
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Facts;
using Xunit;

namespace DDT.Agent.Tests;

// The firmware tables under /sys/firmware, handed on in the layout Windows gives them.
public sealed class LinuxFirmwareTablesTests : IDisposable
{
    private static readonly byte[] s_uuid = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F];

    // System Information: manufacturer, product, version and serial string numbers, the UUID, wake-up type, SKU, family.
    private static readonly byte[] s_table =
    [
        1, 27, 0x00, 0x01, 1, 2, 0, 3, .. s_uuid, 6, 0, 0,
        .. Encoding.ASCII.GetBytes("QEMU\0Standard PC (Q35 + ICH9, 2009)\0DDT-0001\0\0"),
        127, 4, 0x00, 0x02, 0, 0,
    ];

    private readonly LinuxTree _tree = new();

    public void Dispose() => _tree.Dispose();

    // The entry point of SMBIOS 3, 24 bytes: the anchor, checksum, length, then major, minor and document revision.
    private static byte[] EntryPoint3(byte major, byte minor) => [.. "_SM3_"u8, 0, 24, major, minor, 0, 1, .. new byte[13]];

    // The 32-bit entry point, 31 bytes: the anchor, checksum, length, then major and minor, and the BCD revision last.
    private static byte[] EntryPoint2(byte major, byte minor) =>
        [.. "_SM_"u8, 0, 31, major, minor, .. new byte[8], .. "_DMI_"u8, .. new byte[9], (byte)((major << 4) | minor)];

    private LinuxFirmwareTables Tables(byte[] entryPoint)
    {
        _tree.Write(entryPoint, "sys", "firmware", "dmi", "tables", "smbios_entry_point");
        _tree.Write(s_table, "sys", "firmware", "dmi", "tables", "DMI");

        return new LinuxFirmwareTables(_tree.Root);
    }

    [Fact]
    public void WrapsTheTableAsWindowsHandsItOut()
    {
        byte[]? raw = Tables(EntryPoint3(3, 4)).Read(IFirmwareTables.RawSmbiosProvider, 0);

        Assert.NotNull(raw);
        Assert.Equal([0, 3, 4, 0, .. BitConverter.GetBytes(s_table.Length)], raw[..8]);
        Assert.Equal(s_table, raw[8..]);
    }

    // SMBIOS 2.6 turned the first three fields of the UUID round. Windows and Linux both read the entry point's version
    // to tell, so the same table gives the same UUID string on both.
    [Theory]
    [InlineData(3, 4, "03020100-0504-0706-0809-0a0b0c0d0e0f")]
    [InlineData(2, 8, "03020100-0504-0706-0809-0a0b0c0d0e0f")]
    [InlineData(2, 4, "00010203-0405-0607-0809-0a0b0c0d0e0f")]
    public void GivesTheMachineTheUuidWindowsReportsForIt(byte major, byte minor, string expected)
    {
        byte[]? raw = Tables(major >= 3 ? EntryPoint3(major, minor) : EntryPoint2(major, minor)).Read(IFirmwareTables.RawSmbiosProvider, 0);
        byte[] windows = [0, major, minor, 0, .. BitConverter.GetBytes(s_table.Length), .. s_table];

        SmbiosSystemInformation? system = SmbiosParser.TryReadSystemInformation(raw);

        Assert.Equal(expected, system?.Uuid.ToString("D"));
        Assert.Equal(SmbiosParser.TryReadSystemInformation(windows), system);
        Assert.Equal(("QEMU", "Standard PC (Q35 + ICH9, 2009)", "DDT-0001"), (system?.Manufacturer, system?.ProductName, system?.SerialNumber));
    }

    [Fact]
    public void SaysNothingWithoutAnEntryPointItKnowsOrWithoutTheTable()
    {
        LinuxFirmwareTables none = new(_tree.Root);
        Assert.Null(none.Read(IFirmwareTables.RawSmbiosProvider, 0));

        _tree.Write("_XX_ is no entry point"u8.ToArray(), "sys", "firmware", "dmi", "tables", "smbios_entry_point");
        _tree.Write(s_table, "sys", "firmware", "dmi", "tables", "DMI");
        Assert.Null(none.Read(IFirmwareTables.RawSmbiosProvider, 0));
        Assert.Null(none.Read(0x12345678, 0));
    }

    // The kernel numbers a signature that occurs again, and keeps tables it loaded later in folders.
    [Fact]
    public void ListsTheAcpiTablesByTheirSignaturesSoTheTpmIsFound()
    {
        foreach (string name in new[] { "APIC", "FACP", "SSDT1", "SSDT2", "TPM2" })
        {
            _tree.Write([1, 2, 3], "sys", "firmware", "acpi", "tables", name);
        }

        _tree.Write([9], "sys", "firmware", "acpi", "tables", "dynamic", "SSDT9");
        LinuxFirmwareTables tables = new(_tree.Root);

        byte[]? list = tables.List(IFirmwareTables.AcpiProvider);

        Assert.Equal("APICFACPSSDTSSDTTPM2", Encoding.ASCII.GetString(list ?? []));
        Assert.Equal(AcpiTpm.Version20, AcpiTpm.Version(list));
        Assert.Equal([1, 2, 3], tables.Read(IFirmwareTables.AcpiProvider, BitConverter.ToUInt32("TPM2"u8)));
        Assert.Null(tables.Read(IFirmwareTables.AcpiProvider, BitConverter.ToUInt32("TCPA"u8)));
        Assert.Null(tables.Read(IFirmwareTables.AcpiProvider, BitConverter.ToUInt32("../x"u8)));
    }

    [Fact]
    public void CannotListTheAcpiTablesOfAKernelThatShowsNone()
    {
        Assert.Null(new LinuxFirmwareTables(_tree.Root).List(IFirmwareTables.AcpiProvider));
        Assert.Null(new LinuxFirmwareTables(_tree.Root).List(IFirmwareTables.RawSmbiosProvider));
    }
}

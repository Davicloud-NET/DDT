// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class SmbiosParserTests
{
    private static readonly byte[] s_uuidBytes = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F];

    // RawSMBIOSData: calling method, major, minor, DMI revision, then the table length.
    private static byte[] Raw(byte major, byte minor, params byte[][] structures)
    {
        byte[] table = [.. structures.SelectMany(structure => structure)];
        byte[] header = [0, major, minor, 0, .. BitConverter.GetBytes(table.Length)];

        return [.. header, .. table];
    }

    private static byte[] Structure(byte type, byte[] formattedBody, params string[] strings)
    {
        byte[] formatted = [type, (byte)(4 + formattedBody.Length), 0x00, 0x01, .. formattedBody];
        byte[] stringSet = strings.Length == 0
            ? [0, 0]
            : [.. strings.SelectMany(value => Encoding.Latin1.GetBytes(value).Append((byte)0)), 0];

        return [.. formatted, .. stringSet];
    }

    // Type 1 body after the four byte header: manufacturer, product, version and serial string
    // numbers, then the UUID, wake-up type, SKU and family.
    private static byte[] SystemInformation(byte manufacturer = 1, byte product = 2, byte serial = 3) =>
        [manufacturer, product, 0, serial, .. s_uuidBytes, 6, 0, 0];

    // Type 3 body after the four byte header: manufacturer string number, chassis type, version, serial and asset tag
    // string numbers, then the boot-up, power supply and thermal states and the security status.
    private static byte[] SystemEnclosure(byte chassisType) =>
        [1, chassisType, 0, 0, 0, 3, 3, 3, 3];

    [Fact]
    public void ReadsTheSystemStructureAfterOthers()
    {
        byte[] raw = Raw(
            3,
            4,
            Structure(0, [1, 2, 0, 0], "American Megatrends", "1.2.3"),
            Structure(1, SystemInformation(), "Microsoft Corporation", "Virtual Machine", "1234-5678"),
            Structure(127, []));

        SmbiosSystemInformation? system = SmbiosParser.TryReadSystemInformation(raw);

        Assert.NotNull(system);
        Assert.Equal("Microsoft Corporation", system.Manufacturer);
        Assert.Equal("Virtual Machine", system.ProductName);
        Assert.Equal("1234-5678", system.SerialNumber);
    }

    [Fact]
    public void ReadsTheChassisTypeOfAnEnclosureBeforeTheSystemStructure()
    {
        byte[] raw = Raw(
            3,
            4,
            Structure(0, [1, 2, 0, 0], "Dell Inc.", "1.2.3"),
            Structure(3, SystemEnclosure(10), "Dell Inc."),
            Structure(1, SystemInformation(), "Dell Inc.", "Latitude 5440", "ABC1234"),
            Structure(127, []));

        SmbiosSystemInformation? system = SmbiosParser.TryReadSystemInformation(raw);

        Assert.NotNull(system);
        Assert.Equal("Latitude 5440", system.ProductName);
        Assert.Equal((byte)10, system.ChassisType);
    }

    // The top bit is the enclosure's lock flag, not part of the type.
    [Fact]
    public void ReadsTheChassisTypeOfAnEnclosureAfterTheSystemStructureWithoutTheLockBit()
    {
        byte[] raw = Raw(
            3,
            4,
            Structure(1, SystemInformation(), "Lenovo", "ThinkCentre M70q", "PC0ABCDE"),
            Structure(3, SystemEnclosure(0x80 | 35), "Lenovo"),
            Structure(127, []));

        SmbiosSystemInformation? system = SmbiosParser.TryReadSystemInformation(raw);

        Assert.NotNull(system);
        Assert.Equal("Lenovo", system.Manufacturer);
        Assert.Equal((byte)35, system.ChassisType);
    }

    [Fact]
    public void LeavesTheChassisTypeUnknownWithoutAnEnclosure()
    {
        byte[] raw = Raw(3, 4, Structure(1, SystemInformation(), "a", "b", "c"), Structure(127, []));

        SmbiosSystemInformation? system = SmbiosParser.TryReadSystemInformation(raw);

        Assert.NotNull(system);
        Assert.Equal("a", system.Manufacturer);
        Assert.Null(system.ChassisType);
    }

    [Fact]
    public void StopsAtTheEndOfTableStructure()
    {
        byte[] raw = Raw(3, 4, Structure(1, SystemInformation(), "a", "b", "c"), Structure(127, []), Structure(3, SystemEnclosure(9), "a"));

        SmbiosSystemInformation? system = SmbiosParser.TryReadSystemInformation(raw);

        Assert.NotNull(system);
        Assert.Null(system.ChassisType);
    }

    // A broken structure after the system structure costs only the chassis type, as it did before the parser read one.
    [Fact]
    public void KeepsTheSystemStructureWhenAStructureAfterItIsBroken()
    {
        byte[] raw = Raw(3, 4, Structure(1, SystemInformation(), "a", "b", "c"), [3, 200, 0, 1, 0, 0]);

        SmbiosSystemInformation? system = SmbiosParser.TryReadSystemInformation(raw);

        Assert.NotNull(system);
        Assert.Equal("a", system.Manufacturer);
        Assert.Null(system.ChassisType);
    }

    [Fact]
    public void ReadsTheFirstThreeUuidFieldsLittleEndianFromSmbios26()
    {
        byte[] raw = Raw(2, 6, Structure(1, SystemInformation(), "a", "b", "c"));

        // Matches what Windows reports through Win32_ComputerSystemProduct for the same bytes.
        Assert.Equal(Guid.Parse("03020100-0504-0706-0809-0a0b0c0d0e0f"), SmbiosParser.TryReadSystemInformation(raw)!.Uuid);
    }

    [Fact]
    public void ReadsTheUuidInNetworkOrderBeforeSmbios26()
    {
        byte[] raw = Raw(2, 4, Structure(1, SystemInformation(), "a", "b", "c"));

        Assert.Equal(Guid.Parse("00010203-0405-0607-0809-0a0b0c0d0e0f"), SmbiosParser.TryReadSystemInformation(raw)!.Uuid);
    }

    [Fact]
    public void TreatsStringNumberZeroAndBlankStringsAsMissing()
    {
        byte[] raw = Raw(3, 0, Structure(1, SystemInformation(manufacturer: 0, product: 1, serial: 2), "   ", "Serial"));

        SmbiosSystemInformation? system = SmbiosParser.TryReadSystemInformation(raw);

        Assert.NotNull(system);
        Assert.Null(system.Manufacturer);
        Assert.Null(system.ProductName);
        Assert.Equal("Serial", system.SerialNumber);
    }

    [Fact]
    public void SkipsAStructureWithNoStrings()
    {
        byte[] raw = Raw(3, 0, Structure(4, [0, 0, 0, 0]), Structure(1, SystemInformation(), "a", "b", "c"));

        Assert.Equal("a", SmbiosParser.TryReadSystemInformation(raw)?.Manufacturer);
    }

    [Fact]
    public void ReturnsNothingWithoutASystemStructure()
    {
        byte[] raw = Raw(3, 0, Structure(0, [1, 0, 0, 0], "BIOS"), Structure(127, []));

        Assert.Null(SmbiosParser.TryReadSystemInformation(raw));
    }

    [Fact]
    public void NeverReadsPastATruncatedTable()
    {
        byte[] raw = Raw(3, 0, Structure(3, SystemEnclosure(9), "a"), Structure(1, SystemInformation(), "a", "b", "c"));

        for (int length = 0; length < raw.Length; length++)
        {
            _ = SmbiosParser.TryReadSystemInformation(raw.AsSpan(0, length));
        }

        // A structure whose declared length runs past the table is refused rather than read.
        byte[] lying = Raw(3, 0, [1, 200, 0, 1, 0, 0]);
        Assert.Null(SmbiosParser.TryReadSystemInformation(lying));
    }
}

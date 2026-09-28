// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class SmbiosParserTests
{
    // A processor's status: its socket populated and the processor enabled, and an empty socket.
    private const byte Populated = 0x41;
    private const byte Empty = 0x00;

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

    // Type 1 as Lenovo fills it in: manufacturer, product, version and serial, the UUID and wake-up type, then the SKU
    // and the family, which SMBIOS 2.4 added.
    private static byte[] LenovoSystemInformation() =>
        [1, 2, 3, 4, .. s_uuidBytes, 6, 5, 6];

    // Type 3 body after the four byte header: manufacturer string number, chassis type, version, serial and asset tag
    // string numbers, then the boot-up, power supply and thermal states and the security status.
    private static byte[] SystemEnclosure(byte chassisType, byte assetTag = 0) =>
        [1, chassisType, 0, 0, assetTag, 3, 3, 3, 3];

    // Type 0 body of SMBIOS 2.0: vendor and version string numbers, the starting segment, the release date's string
    // number, the ROM size and eight bytes of characteristics.
    private static byte[] BiosInformation(byte version = 2, byte date = 3) =>
        [1, version, 0x00, 0xF0, date, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0];

    // Type 2 body: manufacturer, product, version, serial and asset tag string numbers.
    private static byte[] Baseboard(byte product = 2) =>
        [1, product, 0, 0, 0];

    // Type 4 body of SMBIOS 2.0: socket, type, family, manufacturer, the processor id, the version's string number,
    // voltage, external clock, maximum and current speed, the status and the upgrade.
    private static byte[] Processor(byte version, byte status) =>
        [1, 3, 0xC6, 2, 0, 0, 0, 0, 0, 0, 0, 0, version, 0, 0, 0, 0, 0, 0, 0, status, 1];

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

    [Fact]
    public void ReadsLenovosModelNameFromTheSystemVersionWithTheSkuAndFamily()
    {
        byte[] raw = Raw(
            3,
            4,
            Structure(
                1,
                LenovoSystemInformation(),
                "LENOVO",
                "21HDCTO1WW",
                "ThinkPad T14 Gen 4",
                "PF4ABCDE",
                "LENOVO_MT_21HD_BU_Think_FM_ThinkPad T14 Gen 4",
                "ThinkPad T14 Gen 4"),
            Structure(127, []));

        SmbiosSystemInformation? system = SmbiosParser.TryReadSystemInformation(raw);

        Assert.NotNull(system);
        Assert.Equal("21HDCTO1WW", system.ProductName);
        Assert.Equal("ThinkPad T14 Gen 4", system.Version);
        Assert.Equal("LENOVO_MT_21HD_BU_Think_FM_ThinkPad T14 Gen 4", system.Sku);
        Assert.Equal("ThinkPad T14 Gen 4", system.Family);
        Assert.Equal("PF4ABCDE", system.SerialNumber);
    }

    // SMBIOS 2.1 to 2.3 end the structure after the wake-up type.
    [Fact]
    public void LeavesTheSkuAndFamilyUnknownInASystemStructureFromBeforeSmbios24()
    {
        byte[] raw = Raw(2, 3, Structure(1, [1, 2, 3, 4, .. s_uuidBytes, 6], "a", "b", "1.0", "c", "not the SKU"));

        SmbiosSystemInformation? system = SmbiosParser.TryReadSystemInformation(raw);

        Assert.NotNull(system);
        Assert.Equal("1.0", system.Version);
        Assert.Null(system.Sku);
        Assert.Null(system.Family);
    }

    [Fact]
    public void ReadsTheBiosTheBaseboardTheAssetTagAndTheProcessor()
    {
        byte[] raw = Raw(
            3,
            4,
            Structure(0, BiosInformation(), "LENOVO", "R2EET41W (1.22 )", "05/12/2023"),
            Structure(1, SystemInformation(), "LENOVO", "21HDCTO1WW", "PF4ABCDE"),
            Structure(2, Baseboard(), "LENOVO", "21HDCTO1WW"),
            Structure(3, SystemEnclosure(10, assetTag: 2), "LENOVO", "IT-004711"),
            Structure(4, Processor(3, Populated), "U3E1", "Intel(R) Corporation", "13th Gen Intel(R) Core(TM) i7-1365U   "),
            Structure(127, []));

        SmbiosSystemInformation? system = SmbiosParser.TryReadSystemInformation(raw);

        Assert.NotNull(system);
        Assert.Equal("R2EET41W (1.22 )", system.BiosVersion);
        Assert.Equal("2023-05-12", system.BiosDate);
        Assert.Equal("21HDCTO1WW", system.BaseboardProduct);
        Assert.Equal("IT-004711", system.AssetTag);
        Assert.Equal((byte)10, system.ChassisType);
        Assert.Equal("13th Gen Intel(R) Core(TM) i7-1365U", system.ProcessorVersion);
    }

    [Fact]
    public void LeavesEveryOtherFactUnknownWithOnlyTheSystemStructure()
    {
        byte[] raw = Raw(3, 4, Structure(1, SystemInformation(), "a", "b", "c"), Structure(127, []));

        SmbiosSystemInformation? system = SmbiosParser.TryReadSystemInformation(raw);

        Assert.NotNull(system);
        Assert.Equal(
            (null, null, null, null, null, null, null, null),
            (system.Version, system.Sku, system.Family, system.BiosVersion, system.BiosDate, system.BaseboardProduct, system.AssetTag, system.ProcessorVersion));
    }

    // A string number of 0 names no string, one past the last names nothing, and a blank string says nothing.
    [Fact]
    public void TreatsMissingAndBlankStringsAsUnknown()
    {
        byte[] raw = Raw(
            3,
            4,
            Structure(0, BiosInformation(version: 0, date: 9), "Vendor"),
            Structure(1, [1, 2, 3, 4, .. s_uuidBytes, 6, 5, 0], "a", "b", "   ", "c"),
            Structure(2, Baseboard(product: 0), "Maker"),
            Structure(3, SystemEnclosure(3, assetTag: 2), "Maker", " "),
            Structure(4, Processor(0, Populated), "CPU0", "Maker"),
            Structure(127, []));

        SmbiosSystemInformation? system = SmbiosParser.TryReadSystemInformation(raw);

        Assert.NotNull(system);
        Assert.Equal(
            (null, null, null, null, null, null, null, null),
            (system.Version, system.Sku, system.Family, system.BiosVersion, system.BiosDate, system.BaseboardProduct, system.AssetTag, system.ProcessorVersion));
    }

    // The server knows the placeholders board makers leave in fields they never filled in, and treats them as it does
    // for the model, so the agent reports what the firmware says.
    [Fact]
    public void ReportsPlaceholdersAsTheFirmwareWroteThem()
    {
        byte[] raw = Raw(
            3,
            4,
            Structure(1, LenovoSystemInformation(), "To be filled by O.E.M.", "To be filled by O.E.M.", "Default string", "c", "Default string", "Default string"),
            Structure(3, SystemEnclosure(3, assetTag: 2), "Default string", "Default string"),
            Structure(127, []));

        SmbiosSystemInformation? system = SmbiosParser.TryReadSystemInformation(raw);

        Assert.NotNull(system);
        Assert.Equal("Default string", system.Version);
        Assert.Equal("Default string", system.AssetTag);
    }

    // A server lists its empty sockets as well, and its first socket may be one of them.
    [Fact]
    public void ReadsTheFirstProcessorWhoseSocketHoldsOne()
    {
        byte[] raw = Raw(
            3,
            4,
            Structure(4, Processor(3, Empty), "CPU0", "None", "Not a processor"),
            Structure(4, Processor(3, Populated), "CPU1", "Intel(R) Corporation", "Intel(R) Xeon(R) Gold 6338 CPU @ 2.00GHz"),
            Structure(4, Processor(3, Populated), "CPU2", "Intel(R) Corporation", "The second processor"),
            Structure(1, SystemInformation(), "a", "b", "c"),
            Structure(127, []));

        Assert.Equal("Intel(R) Xeon(R) Gold 6338 CPU @ 2.00GHz", SmbiosParser.TryReadSystemInformation(raw)?.ProcessorVersion);
    }

    [Fact]
    public void LeavesTheProcessorUnknownWhenNoSocketHoldsOne()
    {
        byte[] raw = Raw(3, 4, Structure(4, Processor(3, Empty), "CPU0", "None", "Not a processor"), Structure(1, SystemInformation(), "a", "b", "c"));

        Assert.Null(SmbiosParser.TryReadSystemInformation(raw)?.ProcessorVersion);
    }

    [Fact]
    public void ReadsTheFirstBaseboardAndEnclosure()
    {
        byte[] raw = Raw(
            3,
            4,
            Structure(2, Baseboard(), "Maker", "First board"),
            Structure(2, Baseboard(), "Maker", "Second board"),
            Structure(3, SystemEnclosure(3, assetTag: 2), "Maker", "First tag"),
            Structure(3, SystemEnclosure(9, assetTag: 2), "Maker", "Second tag"),
            Structure(1, SystemInformation(), "a", "b", "c"));

        SmbiosSystemInformation? system = SmbiosParser.TryReadSystemInformation(raw);

        Assert.NotNull(system);
        Assert.Equal(("First board", "First tag", (byte?)3), (system.BaseboardProduct, system.AssetTag, system.ChassisType));
    }

    // SMBIOS 2.3 and later write mm/dd/yyyy; earlier tables mm/dd/yy for 19yy.
    [Theory]
    [InlineData("05/12/2023", "2023-05-12")]
    [InlineData("1/5/2020", "2020-01-05")]
    [InlineData("02/29/2024", "2024-02-29")]
    [InlineData("12/31/99", "1999-12-31")]
    [InlineData("2023-05-12", null)]
    [InlineData("13/01/2020", null)]
    [InlineData("02/30/2020", null)]
    [InlineData("00/00/0000", null)]
    [InlineData("05/12/023", null)]
    [InlineData("+5/12/2023", null)]
    [InlineData("05/12/2023/1", null)]
    public void ReadsTheBiosDateAsAnIsoDate(string written, string? expected)
    {
        byte[] raw = Raw(3, 4, Structure(0, BiosInformation(), "Vendor", "1.0", written), Structure(1, SystemInformation(), "a", "b", "c"));

        Assert.Equal(expected, SmbiosParser.TryReadSystemInformation(raw)?.BiosDate);
    }
}

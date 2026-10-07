// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;
using DDT.Agent.Facts;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class AcpiTpmTests
{
    // What EnumSystemFirmwareTables writes for the ACPI provider: the signatures one after another, letters in order.
    private static byte[] Ids(params string[] signatures) => Encoding.ASCII.GetBytes(string.Concat(signatures));

    [Fact]
    public void FindsATpm20()
    {
        Assert.Equal(AcpiTpm.Version20, AcpiTpm.Version(Ids("FACP", "APIC", "TPM2", "SSDT")));
    }

    [Fact]
    public void FindsATpm12()
    {
        Assert.Equal(AcpiTpm.Version12, AcpiTpm.Version(Ids("FACP", "TCPA", "APIC")));
    }

    [Fact]
    public void TakesATpm20WhenBothTablesAreListed()
    {
        Assert.Equal(AcpiTpm.Version20, AcpiTpm.Version(Ids("TCPA", "FACP", "TPM2")));
    }

    [Fact]
    public void FindsNoTpmWithoutEitherTable()
    {
        Assert.Null(AcpiTpm.Version(Ids("FACP", "APIC", "SSDT", "HPET")));
        Assert.Null(AcpiTpm.Version([]));
    }

    // Windows lists each id as a DWORD in memory. TPM2 is 0x324D5054, and GetSystemFirmwareTable takes that value to
    // read it. The letters in reverse order don't name the table.
    [Fact]
    public void ReadsTheSignaturesInTheOrderOfTheirLetters()
    {
        byte[] listed = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(listed, 0x50434146);
        BinaryPrimitives.WriteUInt32LittleEndian(listed.AsSpan(4), 0x324D5054);

        Assert.Equal("FACPTPM2", Encoding.ASCII.GetString(listed));
        Assert.Equal(AcpiTpm.Version20, AcpiTpm.Version(listed));
        Assert.Null(AcpiTpm.Version(Ids("2MPT", "APCT")));
    }

    [Fact]
    public void LooksOnlyAtWholeIds()
    {
        Assert.Null(AcpiTpm.Version(Ids("FTPM", "2APC")));
        Assert.Null(AcpiTpm.Version(Ids("FACP", "TPM")));
    }

    // The list from a PC with a TPM 2.0, exactly as Windows 11 returned it.
    [Fact]
    public void FindsTheTpmInTheListOfARealMachine()
    {
        byte[] listed = Encoding.ASCII.GetBytes(
            "DBGPMCFGFACPAPICHPETFPDTFIDTSSDTHWINSSDTSSDTSSDTSSDTSSDTSSDTSSDTNHLTLPITSSDTSSDTDBG2SSDTSSDTSSDTSSDTBGRTWPBTPHATTPM2WSMT");

        Assert.Equal(AcpiTpm.Version20, AcpiTpm.Version(listed));
    }
}

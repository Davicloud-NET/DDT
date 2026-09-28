// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;
using DDT.Agent.Deployment;
using DDT.Agent.Facts;
using DDT.Contracts.Machines;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class MachineFactsReaderTests
{
    private static readonly SmbiosSystemInformation s_lenovo = new(Guid.Empty, "LENOVO", "21HDCTO1WW", "PF4ABCDE", 10)
    {
        Version = "ThinkPad T14 Gen 4",
        Sku = "LENOVO_MT_21HD_BU_Think_FM_ThinkPad T14 Gen 4",
        Family = "ThinkPad T14 Gen 4",
        BiosVersion = "R2EET41W (1.22 )",
        BiosDate = "2023-05-12",
        BaseboardProduct = "21HDCTO1WW",
        AssetTag = "IT-004711",
        ProcessorVersion = "13th Gen Intel(R) Core(TM) i7-1365U",
    };

    private static readonly NetworkFacts s_network = new("10.1.2.3", 24, "10.1.2.1", "corp.example.com", "10.1.0.5");

    private readonly FakeFirmwareTables _firmware = new();
    private readonly FakeSystemHardware _hardware = new();
    private readonly FakeUefiVariables _uefi = new();

    private MachineFacts Read(SmbiosSystemInformation? smbios = null, NetworkFacts? network = null) =>
        new MachineFactsReader(_firmware, _hardware, _uefi).Read(smbios, network);

    private static byte[] AcpiIds(params string[] signatures) => Encoding.ASCII.GetBytes(string.Concat(signatures));

    // Four cores with two logical processors each, as GetLogicalProcessorInformationEx lays them out.
    private static byte[] FourCores()
    {
        byte[] records = new byte[4 * 48];

        for (int core = 0; core < 4; core++)
        {
            Span<byte> record = records.AsSpan(core * 48, 48);
            BinaryPrimitives.WriteUInt32LittleEndian(record[4..], 48);
            BinaryPrimitives.WriteUInt16LittleEndian(record[30..], 1);
            BinaryPrimitives.WriteUInt64LittleEndian(record[32..], 0b11UL << (core * 2));
        }

        return records;
    }

    [Fact]
    public void ReadsEveryFact()
    {
        _hardware.Installed = () => 16UL * 1024 * 1024;
        _hardware.Cores = FourCores;
        _firmware.Lists[FirmwareTables.AcpiProvider] = AcpiIds("FACP", "APIC", "TPM2");
        _uefi.Values["SecureBoot"] = [1];

        MachineFacts facts = Read(s_lenovo, s_network);

        Assert.Equal(
            new MachineFacts
            {
                MemoryMegabytes = 16384,
                ProcessorName = "13th Gen Intel(R) Core(TM) i7-1365U",
                ProcessorCores = 4,
                LogicalProcessors = 8,
                TpmPresent = true,
                TpmVersion = "2.0",
                SecureBootCapable = true,
                IPv4Address = "10.1.2.3",
                IPv4PrefixLength = 24,
                DefaultGateway = "10.1.2.1",
                DnsSuffix = "corp.example.com",
                DhcpServer = "10.1.0.5",
                SystemVersion = "ThinkPad T14 Gen 4",
                SystemFamily = "ThinkPad T14 Gen 4",
                SystemSku = "LENOVO_MT_21HD_BU_Think_FM_ThinkPad T14 Gen 4",
                AssetTag = "IT-004711",
                BaseboardProduct = "21HDCTO1WW",
                BiosVersion = "R2EET41W (1.22 )",
                BiosDate = "2023-05-12",
            },
            facts);
    }

    [Fact]
    public void ReportsTheInstalledMemoryRatherThanWhatWindowsCanUse()
    {
        _hardware.Installed = () => 8UL * 1024 * 1024;
        _hardware.Usable = () => 7_900UL * 1024 * 1024;

        Assert.Equal(8192, Read().MemoryMegabytes);
    }

    [Fact]
    public void FallsBackToTheMemoryWindowsCanUseWhenTheInstalledMemoryIsUnknown()
    {
        _hardware.Usable = () => 4_000UL * 1024 * 1024;

        Assert.Equal(4000, Read().MemoryMegabytes);

        _hardware.Installed = () => 0;

        Assert.Equal(4000, Read().MemoryMegabytes);

        _hardware.Installed = () => throw new Win32Exception(13);

        Assert.Equal(4000, Read().MemoryMegabytes);
    }

    [Fact]
    public void LeavesTheMemoryUnknownWhenWindowsCannotTell()
    {
        Assert.Null(Read().MemoryMegabytes);

        _hardware.Usable = () => throw new Win32Exception(87);

        Assert.Null(Read().MemoryMegabytes);
    }

    [Theory]
    [InlineData("FACP APIC TPM2", true, "2.0")]
    [InlineData("FACP TCPA", true, "1.2")]
    [InlineData("FACP APIC SSDT", false, null)]
    public void FindsTheTpmInTheAcpiTables(string tables, bool present, string? version)
    {
        _firmware.Lists[FirmwareTables.AcpiProvider] = AcpiIds(tables.Split(' '));

        MachineFacts facts = Read();

        Assert.Equal((present, version), (facts.TpmPresent, facts.TpmVersion));
    }

    [Fact]
    public void LeavesTheTpmUnknownWhenTheAcpiTablesCannotBeListed()
    {
        MachineFacts facts = Read();

        Assert.Equal((null, null), (facts.TpmPresent, facts.TpmVersion));

        _firmware.Failure = new Win32Exception(1);
        facts = Read();

        Assert.Equal((null, null), (facts.TpmPresent, facts.TpmVersion));
    }

    [Fact]
    public void IsSecureBootCapableWhenTheFirmwareDefinesTheSecureBootVariable()
    {
        _uefi.Values["SecureBoot"] = [0];

        Assert.True(Read().SecureBootCapable);

        _uefi.Values.Clear();

        Assert.False(Read().SecureBootCapable);
    }

    [Fact]
    public void LeavesSecureBootCapableUnknownWhenTheVariablesCannotBeRead()
    {
        _uefi.Failure = new DeploymentStepException("This machine did not start in UEFI mode, so its firmware variables cannot be read.");

        Assert.Null(Read().SecureBootCapable);
    }

    [Fact]
    public void LeavesTheProcessorCountsUnknownWhenWindowsCannotTell()
    {
        MachineFacts facts = Read();

        Assert.Equal((null, null), (facts.ProcessorCores, facts.LogicalProcessors));

        _hardware.Cores = () => [1, 2, 3];
        facts = Read();

        Assert.Equal((null, null), (facts.ProcessorCores, facts.LogicalProcessors));
    }

    // One fact that cannot be read costs only that fact.
    [Fact]
    public void KeepsEveryOtherFactWhenReadingOneFails()
    {
        _hardware.Installed = () => throw new InvalidOperationException("installed");
        _hardware.Usable = () => throw new InvalidOperationException("usable");
        _hardware.Cores = () => throw new InvalidOperationException("cores");
        _firmware.Failure = new InvalidOperationException("firmware");
        _uefi.Failure = new UnauthorizedAccessException("uefi");

        MachineFacts facts = Read(s_lenovo, s_network);

        Assert.Equal(
            (null, null, null, null, null, null),
            (facts.MemoryMegabytes, facts.ProcessorCores, facts.LogicalProcessors, facts.TpmPresent, facts.TpmVersion, facts.SecureBootCapable));
        Assert.Equal(("13th Gen Intel(R) Core(TM) i7-1365U", "ThinkPad T14 Gen 4", "10.1.2.3"), (facts.ProcessorName, facts.SystemVersion, facts.IPv4Address));
    }

    [Fact]
    public void LeavesTheTableAndNetworkFactsUnknownWithoutThem()
    {
        MachineFacts facts = Read();

        Assert.Equal(
            (null, null, null, null, null, null, null, null, null),
            (facts.ProcessorName, facts.SystemVersion, facts.SystemFamily, facts.SystemSku, facts.AssetTag, facts.BaseboardProduct, facts.BiosVersion, facts.BiosDate, facts.IPv4Address));
    }

    // The server keeps no more than AgentLimits.MaxFactLength characters of a text.
    [Fact]
    public void CutsLongTextsAndReplacesControlCharacters()
    {
        SmbiosSystemInformation smbios = s_lenovo with
        {
            AssetTag = new string('A', 300),
            Version = "ThinkPad\tT14\u0001",
            Family = "\u0007 ",
        };

        MachineFacts facts = Read(smbios, s_network with { DnsSuffix = new string('d', 200) });

        Assert.Equal(new string('A', AgentLimits.MaxFactLength), facts.AssetTag);
        Assert.Equal("ThinkPad T14", facts.SystemVersion);
        Assert.Null(facts.SystemFamily);
        Assert.Equal(AgentLimits.MaxFactLength, facts.DnsSuffix?.Length);
    }
}

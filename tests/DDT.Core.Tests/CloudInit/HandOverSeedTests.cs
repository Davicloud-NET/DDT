// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using Xunit;

namespace DDT.Core.Tests.CloudInit;

// The seed as a hand-over to the installed Linux lays it out: the administrator's files as they were, and beside them
// the script and what it installs.
public sealed class HandOverSeedTests
{
    private const uint Serial = 0x26A1C0DE;
    private const long FirstSector = 16_644_096;
    private const int MaxBytes = 32 * 1024 * 1024;

    // The size of the agent the spike put on its seeds.
    private const int AgentBytes = 19_680_128;

    private static readonly DateTime s_written = new(2026, 10, 11, 9, 30, 0, DateTimeKind.Utc);

    private static readonly CloudInitSeedFiles s_files = new(
        "instance-id: \"4c4c4544-0042-3510-8052-b4c04f4d3232\"\nlocal-hostname: \"PC-042\"\n",
        "#cloud-config\nusers:\n  - default\n",
        "version: 2\n");

    private static readonly byte[] s_agent = Content(AgentBytes, 1);
    private static readonly byte[] s_configuration = "{\"serverUrl\":\"https://ddt.example:8443/\"}"u8.ToArray();
    private static readonly byte[] s_state = Content(3000, 2);
    private static readonly byte[] s_token = "CfDJ8-a-run-token-of-the-server-0123456789"u8.ToArray();

    private static byte[] Content(int length, int seed)
    {
        byte[] content = new byte[length];
        new Random(seed).NextBytes(content);

        return content;
    }

    private static IReadOnlyList<SeedFile> Added() => LinuxAgentFiles.ForSeed(s_agent, s_configuration, s_state, s_token);

    // The seed's own files and then what a hand-over adds, as the hand-over lists them.
    private static IReadOnlyList<SeedFile> HandedOver(CloudInitSeedFiles files, IReadOnlyList<SeedFile>? added = null) =>
        [.. CloudInitSeed.FilesOf(files), .. added ?? Added()];

    private static FatVolume Open(byte[] volume) => FatVolume.Open(new MemoryStream(volume), 0, volume.Length);

    private static byte[] Read(FatVolume volume, string path) => volume.ReadFile(volume.Find(path)!, MaxBytes);

    [Fact]
    public void PutsTheScriptAndWhatItInstallsBesideTheAdministratorsFiles()
    {
        FatVolume volume = Open(CloudInitSeed.Build(HandedOver(s_files), Serial, s_written, FirstSector));

        Assert.Equal(["meta-data", "user-data", "network-config", "vendor-data", "ddt"], volume.List("").Select(entry => entry.Name));
        Assert.Equal(["ddt-agent", "agent.json", "state.json", "token"], volume.List(LinuxAgentFiles.SeedFolder).Select(entry => entry.Name));
        Assert.Equal(VendorDataScript.Bytes(), Read(volume, CloudInitSeed.VendorData));
        Assert.Equal(s_agent, Read(volume, LinuxAgentFiles.SeedAgent));
        Assert.Equal(s_configuration, Read(volume, LinuxAgentFiles.SeedConfiguration));
        Assert.Equal(s_state, Read(volume, LinuxAgentFiles.SeedState));
        Assert.Equal(s_token, Read(volume, LinuxAgentFiles.SeedToken));
    }

    // cloud-init knows the seed again by its label, and the installed Linux by the serial number blkid shows.
    [Fact]
    public void LeavesTheAdministratorsFilesAndTheVolumesNameAsTheyWere()
    {
        byte[] before = CloudInitSeed.Build(s_files, Serial, s_written, FirstSector);
        byte[] after = CloudInitSeed.Build(HandedOver(s_files), Serial, s_written, FirstSector);
        FatVolume plain = Open(before);
        FatVolume handedOver = Open(after);

        Assert.Equal(before.Length, after.Length);
        Assert.Equal((FatType.Fat16, "CIDATA", Serial), (handedOver.Type, handedOver.Label, handedOver.SerialNumber));
        Assert.Equal((plain.Type, plain.Label, plain.SerialNumber, plain.ClusterBytes), (handedOver.Type, handedOver.Label, handedOver.SerialNumber, handedOver.ClusterBytes));
        Assert.Equal((uint)FirstSector, BinaryPrimitives.ReadUInt32LittleEndian(after.AsSpan(28)));

        foreach (string name in new[] { CloudInitSeed.MetaData, CloudInitSeed.UserData, CloudInitSeed.NetworkConfig })
        {
            Assert.Equal(plain.Find(name), handedOver.Find(name));
            Assert.Equal(Read(plain, name), Read(handedOver, name));
        }

        Assert.Equal(s_files.UserData, Encoding.UTF8.GetString(Read(handedOver, CloudInitSeed.UserData)));
    }

    // A seed without a network-config gets none from the hand-over either.
    [Fact]
    public void AddsNoFileOfCloudInitsThatTheAdministratorLeftOut()
    {
        FatVolume volume = Open(CloudInitSeed.Build(HandedOver(s_files with { NetworkConfig = null }), Serial, s_written, FirstSector));

        Assert.Equal(["meta-data", "user-data", "vendor-data", "ddt"], volume.List("").Select(entry => entry.Name));
    }

    // Each file takes whole clusters, and the folder one more. The agent and its files leave two thirds of the seed.
    [Fact]
    public void SaysHowMuchOfTheSeedIsLeft()
    {
        FatVolume plain = Open(CloudInitSeed.Build(s_files, Serial, s_written, FirstSector));
        FatVolume handedOver = Open(CloudInitSeed.Build(HandedOver(s_files), Serial, s_written, FirstSector));
        int cluster = plain.ClusterBytes;
        long added = cluster + Added().Sum(file => (file.Content.LongLength + cluster - 1) / cluster * cluster);

        Assert.Equal(1024, cluster);
        Assert.Equal(plain.FreeBytes, CloudInitSeed.FreeBytes(CloudInitSeed.FilesOf(s_files)));
        Assert.Equal(handedOver.FreeBytes, CloudInitSeed.FreeBytes(HandedOver(s_files)));
        Assert.Equal(added, plain.FreeBytes - handedOver.FreeBytes);
        Assert.InRange(handedOver.FreeBytes, 44L * 1024 * 1024, 48L * 1024 * 1024);
    }

    [Fact]
    public void SaysByHowMuchFilesDoNotFitAndBuildsNoSuchSeed()
    {
        // The folder takes a cluster of its own.
        const int Cluster = 1024;
        long room = CloudInitSeed.FreeBytes(CloudInitSeed.FilesOf(s_files));
        SeedFile[] full = [new(LinuxAgentFiles.SeedAgent, new byte[room - Cluster])];
        SeedFile[] tooLarge = [new(LinuxAgentFiles.SeedAgent, new byte[room - Cluster + 1])];

        Assert.Equal(0, CloudInitSeed.FreeBytes(HandedOver(s_files, full)));
        Assert.Equal(-Cluster, CloudInitSeed.FreeBytes(HandedOver(s_files, tooLarge)));
        Assert.Equal(0, Open(CloudInitSeed.Build(HandedOver(s_files, full), Serial, s_written, FirstSector)).FreeBytes);
        Assert.Throws<InvalidOperationException>(() => CloudInitSeed.Build(HandedOver(s_files, tooLarge), Serial, s_written, FirstSector));
    }
}

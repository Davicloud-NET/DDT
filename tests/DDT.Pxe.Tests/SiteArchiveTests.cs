// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
using System.Text;
using DDT.Core.Boot;
using Xunit;

namespace DDT.Pxe.Tests;

public sealed class SiteArchiveTests
{
    private const string Root = "-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----\n";

    private static readonly DateTimeOffset s_now = DateTimeOffset.FromUnixTimeSeconds(1_791_460_800);

    private static LinuxBootBundle Bundle() =>
        new("/app/linux-boot", FrozenDictionary<string, BootFileStamp>.Empty, new Uri("https://ddt-server.example.test:8443"), Root) { KeyboardLayout = "German (de)" };

    // The kernel unpacks it over the agent's folder, so it holds that folder and the one file, as root's.
    [Fact]
    public void HoldsTheAgentsFolderAndItsConfigurationAndNothingElse()
    {
        List<(string Path, uint Mode, byte[] Content)> entries = CpioArchive.Entries(SiteArchive.Write(Bundle(), s_now));

        Assert.Equal(2, entries.Count);
        Assert.Equal(("ddt", 0x41EDu), (entries[0].Path, entries[0].Mode));
        Assert.Equal(("ddt/agent.json", 0x81A4u), (entries[1].Path, entries[1].Mode));
        Assert.Equal(
            AgentConfigurationFile.Write(new Uri("https://ddt-server.example.test:8443"), Root, "German (de)"),
            entries[1].Content);
    }

    [Fact]
    public void IsTheSameBytesForTheSameValuesAtTheSameTime()
    {
        Assert.Equal(SiteArchive.Write(Bundle(), s_now), SiteArchive.Write(Bundle(), s_now));
        Assert.NotEqual(SiteArchive.Write(Bundle(), s_now), SiteArchive.Write(Bundle(), s_now.AddSeconds(1)));
    }

    // Everyone on the network may read it: the address, the public root and the layout's name are all it says.
    [Fact]
    public void CarriesNoWordOfTheBundleBeyondWhatTheAgentReads()
    {
        LinuxBootBundle bundle = Bundle() with { KernelArguments = "console=ttyS0 marker-arguments", Keymap = "de" };
        string text = Encoding.Latin1.GetString(SiteArchive.Write(bundle, s_now));

        Assert.DoesNotContain("marker-arguments", text, StringComparison.Ordinal);
        Assert.DoesNotContain("linux-boot", text, StringComparison.Ordinal);
        Assert.Contains("\"serverUrl\":\"https://ddt-server.example.test:8443\"", text, StringComparison.Ordinal);
    }
}

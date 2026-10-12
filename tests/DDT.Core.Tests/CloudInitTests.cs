// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;
using DDT.Contracts.Sequences;
using DDT.Core.CloudInit;
using DDT.Core.Disks;
using Xunit;

namespace DDT.Core.Tests;

public sealed class CloudInitTests
{
    private static readonly Dictionary<string, string?> s_values = new()
    {
        [MachineVariableNames.ComputerName] = "PC-01",
        [MachineVariableNames.Manufacturer] = "Dell Inc.",
        [MachineVariableNames.Model] = "OptiPlex \"7010\" \\ tower",
        [MachineVariableNames.SerialNumber] = "ABC\t123",
        [MachineVariableNames.SmbiosUuid] = "4c4c4544-0042-3510-8051-b4c04f4e3332",
        [MachineVariableNames.MacAddress] = "00:15:5d:01:02:03",
    };

    [Fact]
    public void FillsThePlaceholdersIgnoringCaseAndSpaces()
    {
        string rendered = CloudInitTemplate.Render(
            "instance-id: \"{{SmbiosUuid}}\"\nlocal-hostname: \"{{ computername }}\"\nmac: \"{{MACADDRESS}}\"\n",
            s_values);

        Assert.Equal(
            "instance-id: \"4c4c4544-0042-3510-8051-b4c04f4e3332\"\nlocal-hostname: \"PC-01\"\nmac: \"00:15:5d:01:02:03\"\n",
            rendered);
    }

    [Fact]
    public void EscapesValuesForADoubleQuotedYamlString()
    {
        string rendered = CloudInitTemplate.Render("model: \"{{Model}}\" serial: \"{{SerialNumber}}\"", s_values);

        Assert.Equal("model: \"OptiPlex \\\"7010\\\" \\\\ tower\" serial: \"ABC\\x09123\"", rendered);
    }

    // Firmware strings are read as Latin-1, so bytes 0x80 to 0x9F become C1 control characters, which YAML refuses raw.
    [Fact]
    public void EscapesTheC1ControlCharactersToo()
    {
        Dictionary<string, string?> values = new() { [MachineVariableNames.Model] = "A\u0085B\u009FC D" };

        Assert.Equal("model: \"A\\x85B\\x9fC D\"", CloudInitTemplate.Render("model: \"{{Model}}\"", values));
    }

    // Render turns every kind of line end into a line feed, also U+2028 and U+2029, and so does YAML. A value that
    // holds one must not start a line of its own in the seed: that line would be cloud-config of whoever set the value.
    [Theory]
    [InlineData("\u2028", "\\u2028")]
    [InlineData("\u2029", "\\u2029")]
    [InlineData("\u0085", "\\x85")]
    [InlineData("\n", "\\x0a")]
    [InlineData("\r\n", "\\x0d\\x0a")]
    [InlineData("\f", "\\x0c")]
    public void AValueNeverMakesALineOfItsOwn(string lineEnd, string escaped)
    {
        Dictionary<string, string?> values = new(s_values)
        {
            [MachineVariableNames.Model] = $"OptiPlex{lineEnd}runcmd: [reboot]",
            ["Office"] = $"Wien{lineEnd}ssh_pwauth: true",
        };

        string rendered = CloudInitTemplate.Render("#cloud-config\r\nmodel: \"{{Model}}\"\r\noffice: {{Office}}\r\n", values);

        Assert.Equal($"#cloud-config\nmodel: \"OptiPlex{escaped}runcmd: [reboot]\"\noffice: Wien{escaped}ssh_pwauth: true\n", rendered);
        Assert.Equal(3, rendered.Count(character => character == '\n'));
        Assert.Equal(-1, rendered.AsSpan().IndexOfAny("\r\f\u0085\u2028\u2029"));
    }

    [Fact]
    public void LeavesJinjaAndUnknownNamesAsTheyAre()
    {
        const string template = "## template: jinja\n#cloud-config\nhostname: {{ v1.local_hostname }}\nfqdn: {{Hostname}}.example\n";

        Assert.Equal(template, CloudInitTemplate.Render(template, s_values));
        Assert.Equal(["Hostname"], CloudInitTemplate.Placeholders(template));
        Assert.Null(CloudInitTemplate.Known("Hostname"));
        Assert.Equal(MachineVariableNames.ComputerName, CloudInitTemplate.Known("COMPUTERNAME"));
    }

    // The run's values come along with the machine's, and a seed may use them by name, ignoring case, with filters. A
    // name without a value stays as it is, just like cloud-init's own placeholders.
    [Fact]
    public void FillsInTheRunsValuesAndLeavesNamesWithoutOne()
    {
        Dictionary<string, string?> values = new(s_values) { ["Office"] = "Wien \"1\"" };

        Assert.Equal(
            "office: \"WIEN \\\"1\\\"\"\nfqdn: {{Hostname}}.example\nhostname: {{ v1.local_hostname }}\n",
            CloudInitTemplate.Render("office: \"{{ office | upper }}\"\nfqdn: {{Hostname}}.example\nhostname: {{ v1.local_hostname }}\n", values));
    }

    [Fact]
    public void NamesAPlaceholderTheMachineHasNoValueFor()
    {
        Dictionary<string, string?> unnamed = new(s_values) { [MachineVariableNames.ComputerName] = null };

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => CloudInitTemplate.Render("hostname: {{ComputerName}}", unnamed));

        Assert.Equal("The machine has no value for {{ComputerName}}.", refusal.Message);
    }

    [Fact]
    public void ListsEachPlaceholderOnceAndWritesLineFeeds()
    {
        Assert.Equal(["Model", "ComputerName"], CloudInitTemplate.Placeholders("{{Model}} {{ ComputerName }} {{model}} {{ }} {{1x}}"));
        Assert.Equal("a\nb\nc", CloudInitTemplate.Render("a\r\nb\rc", s_values));
    }

    [Fact]
    public void BuildsASeedCloudInitFindsByItsLabel()
    {
        byte[] seed = CloudInitSeed.Build(
            new CloudInitSeedFiles("instance-id: a\n", "#cloud-config\nhostname: \"PC-01\"\n", "version: 2\n"),
            0xC0FFEE,
            new DateTime(2026, 9, 25),
            1234);
        FatVolume volume = FatVolume.Open(new MemoryStream(seed), 0, seed.Length);

        Assert.Equal(CloudInitSeed.SizeBytes, seed.Length);
        Assert.Equal(FatType.Fat16, volume.Type);
        Assert.Equal("CIDATA", volume.Label);
        Assert.Equal(1234u, BinaryPrimitives.ReadUInt32LittleEndian(seed.AsSpan(28)));
        Assert.Equal(["meta-data", "user-data", "network-config"], volume.List("").Select(entry => entry.Name));
        Assert.Equal("#cloud-config\nhostname: \"PC-01\"\n", Encoding.UTF8.GetString(volume.ReadFile(volume.Find("user-data")!, 4096)));
        Assert.Equal("version: 2\n", Encoding.UTF8.GetString(volume.ReadFile(volume.Find("network-config")!, 4096)));
    }

    [Fact]
    public void LeavesOutANetworkConfigurationThatIsNotGiven()
    {
        byte[] seed = CloudInitSeed.Build(new CloudInitSeedFiles("instance-id: a\n", "#cloud-config\n", null), 1, new DateTime(2026, 9, 25), 0);

        Assert.Equal(["meta-data", "user-data"], FatVolume.Open(new MemoryStream(seed), 0, seed.Length).List("").Select(entry => entry.Name));
    }
}

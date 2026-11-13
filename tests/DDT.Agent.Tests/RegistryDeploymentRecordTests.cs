// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;
using DDT.Agent.WindowsPhase;
using Microsoft.Win32;
using Xunit;

namespace DDT.Agent.Tests;

// The record as the registry holds it, under a test key in the current user's hive, which the test deletes.
public sealed class RegistryDeploymentRecordTests : IDisposable
{
    private static readonly AgentLog s_log = new(new ImmediateTimeProvider(), TextWriter.Null);

    private readonly string _path = $@"Software\DDT-test-{Guid.NewGuid():N}";
    private readonly RegistryKey _root;

    public RegistryDeploymentRecordTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_path);
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
    }

    private static DeploymentRecordEntry Entry(string? imageName = "Windows 11 Pro") => new(
        "Install Windows 11",
        Guid.Parse("0193a4b2-0000-7000-8000-0000000000c1"),
        12,
        Guid.Parse("0193a4b2-0000-7000-8000-0000000000c2"),
        "Done",
        new DateTimeOffset(2026, 11, 13, 17, 5, 9, TimeSpan.FromHours(1)),
        imageName,
        "26.1.500",
        "ddt.corp.example");

    private Dictionary<string, (object? Value, RegistryValueKind Kind)> Written()
    {
        using RegistryKey key = Assert.IsType<RegistryKey>(_root.OpenSubKey(RegistryDeploymentRecord.KeyPath));

        return key.GetValueNames().ToDictionary(name => name, name => (key.GetValue(name), key.GetValueKind(name)));
    }

    [Fact]
    public async Task WritesEveryValueAsText()
    {
        await new RegistryDeploymentRecord(_root, s_log).WriteAsync(Entry(), TestContext.Current.CancellationToken);

        Dictionary<string, (object? Value, RegistryValueKind Kind)> written = Written();

        Assert.Equal(
            new Dictionary<string, object?>
            {
                ["SequenceName"] = "Install Windows 11",
                ["SequenceId"] = "0193a4b2-0000-7000-8000-0000000000c1",
                ["SequenceRevision"] = "12",
                ["RunId"] = "0193a4b2-0000-7000-8000-0000000000c2",
                ["Result"] = "Done",
                ["DeployedUtc"] = "2026-11-13T16:05:09Z",
                ["ImageName"] = "Windows 11 Pro",
                ["DdtVersion"] = "26.1.500",
                ["ServerName"] = "ddt.corp.example",
            },
            written.ToDictionary(pair => pair.Key, pair => pair.Value.Value));
        Assert.All(written.Values, value => Assert.Equal(RegistryValueKind.String, value.Kind));
    }

    [Fact]
    public async Task ReplacesWhatAnEarlierRunLeft()
    {
        RegistryDeploymentRecord record = new(_root, s_log);
        await record.WriteAsync(Entry(), TestContext.Current.CancellationToken);

        await record.WriteAsync(Entry(imageName: null), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("ImageName", Written().Keys);
    }
}

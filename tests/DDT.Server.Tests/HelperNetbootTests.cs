// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Text;
using DDT.Host.Helper;
using DDT.Pxe;
using DDT.Server.BootImage;
using Xunit;

namespace DDT.Server.Tests;

// What the helper does to the DHCP server and WDS as SYSTEM, with a process of the test's own in place of PowerShell.
public sealed class HelperNetbootTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ddt-helper-netboot-" + Guid.NewGuid().ToString("N"));
    private readonly HelperPaths _paths;
    private readonly List<ProcessStartInfo> _started = [];
    private readonly List<string> _lines = [];
    private int _exitCode;

    public HelperNetbootTests() =>
        _paths = new HelperPaths(Path.Combine(_root, "program"), Path.Combine(_root, "store"), Path.Combine(_root, "store", "boot"), Path.Combine(_root, "work"));

    [Fact]
    public async Task TheOptionsReachPowerShellAsValuesAndNeverAsScript()
    {
        HelperRequest request = new()
        {
            Kind = HelperRequest.DhcpOptions,
            Scopes = ["10.0.100.0", "192.168.1.0"],
            BootServer = "deploy01.contoso.local",
            BootFile = "x64/bootmgfw.efi",
        };

        Assert.Null(await Netboot().RunAsync(request, _lines.Add, TestContext.Current.CancellationToken));

        ProcessStartInfo started = Assert.Single(_started);
        Assert.EndsWith("powershell.exe", started.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(NetbootScripts.DhcpOptions, Script(started), StringComparison.Ordinal);
        Assert.Equal(
            ("10.0.100.0,192.168.1.0", "deploy01.contoso.local", "x64/bootmgfw.efi"),
            (started.Environment["DDT_SCOPES"], started.Environment["DDT_BOOT_SERVER"], started.Environment["DDT_BOOT_FILE"]));
        Assert.Equal(["PowerShell ran"], _lines);
    }

    [Theory]
    [InlineData("10.0.100.0; Remove-Item C:\\", "deploy01", "x64/bootmgfw.efi")]
    [InlineData("010.0.100.0", "deploy01", "x64/bootmgfw.efi")]
    [InlineData("fe80::1", "deploy01", "x64/bootmgfw.efi")]
    [InlineData("10.0.100.0", "deploy01'; calc", "x64/bootmgfw.efi")]
    [InlineData("10.0.100.0", "", "x64/bootmgfw.efi")]
    [InlineData("10.0.100.0", "deploy01", "..\\..\\secret")]
    [InlineData("10.0.100.0", "deploy01", "boot file.efi")]
    public async Task ARequestThatSaysAnythingElseRunsNothing(string scope, string server, string file)
    {
        HelperRequest request = new() { Kind = HelperRequest.DhcpOptions, Scopes = [scope], BootServer = server, BootFile = file };

        Assert.NotNull(await Netboot().RunAsync(request, _lines.Add, TestContext.Current.CancellationToken));
        Assert.Empty(_started);
    }

    [Fact]
    public async Task WdsGetsTheBuildThatIsServedAndARefreshWithoutOneDoesNothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Assert.Contains("no boot image yet", await Netboot().RunAsync(new HelperRequest { Kind = HelperRequest.WdsBootImage }, _lines.Add, cancellationToken), StringComparison.Ordinal);
        Assert.Null(await Netboot().RunAsync(new HelperRequest { Kind = HelperRequest.WdsRefresh }, _lines.Add, cancellationToken));
        Assert.Empty(_started);

        string image = Path.Combine(Directory.CreateDirectory(Path.Combine(BootBuilds.FolderOf(_paths.BootDirectory, "20261002-080000"), "Boot")).FullName, "boot.wim");
        await File.WriteAllTextAsync(image, "the image", cancellationToken);
        BootBuilds.SetCurrent(_paths.BootDirectory, "20261002-080000");

        Assert.Null(await Netboot().RunAsync(new HelperRequest { Kind = HelperRequest.WdsRefresh }, _lines.Add, cancellationToken));
        Assert.Null(await Netboot().RunAsync(new HelperRequest { Kind = HelperRequest.WdsBootImage }, _lines.Add, cancellationToken));

        Assert.All(_started, started => Assert.Contains(NetbootScripts.WdsBootImage, Script(started), StringComparison.Ordinal));
        Assert.Equal(2, _started.Count);
        Assert.Equal([(image, "1"), (image, "0")], _started.Select(started => (started.Environment["DDT_WIM"], started.Environment["DDT_ONLY_REPLACE"])));
        Assert.Equal(NetbootScripts.ImageName, _started[0].Environment["DDT_IMAGE_NAME"]);
    }

    [Fact]
    public async Task WhatPowerShellSaysIsTheReasonWhenItFails()
    {
        _exitCode = 1;

        string? problem = await Netboot().RunAsync(new HelperRequest { Kind = HelperRequest.WdsReplace }, _lines.Add, TestContext.Current.CancellationToken);

        Assert.Equal("PowerShell ran", problem);
        Assert.Contains(NetbootScripts.WdsReplace, Script(Assert.Single(_started)), StringComparison.Ordinal);
        Assert.NotNull(await Netboot().RunAsync(new HelperRequest { Kind = "format-disk" }, _lines.Add, TestContext.Current.CancellationToken));
        Assert.Single(_started);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private HelperNetboot Netboot() => new(_paths, RunAsync);

    // The script as PowerShell decodes it
    private static string Script(ProcessStartInfo started) =>
        Encoding.Unicode.GetString(Convert.FromBase64String(started.ArgumentList[started.ArgumentList.IndexOf("-EncodedCommand") + 1]));

    private Task<int> RunAsync(ProcessStartInfo start, Action<string> line, CancellationToken cancellationToken)
    {
        _started.Add(start);
        line("PowerShell ran");

        return Task.FromResult(_exitCode);
    }
}

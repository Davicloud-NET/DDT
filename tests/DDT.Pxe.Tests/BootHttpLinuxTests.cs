// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace DDT.Pxe.Tests;

// The Linux boot environment over the HTTP boot port: GRUB loads the kernel and the three initrds from there.
public sealed class BootHttpLinuxTests : IAsyncLifetime
{
    private const int BootPort = 8080;

    private readonly string _root = Directory.CreateTempSubdirectory("ddt-http-linux-").FullName;
    private readonly byte[] _kernel = [.. Enumerable.Range(0, 70_000).Select(value => (byte)(value * 7))];
    private readonly FakeLinuxBootSite _site = new();
    private readonly TestClock _clock = new(DateTimeOffset.FromUnixTimeSeconds(1_791_460_800));
    private readonly List<string> _changed = [];

    private WebApplication? _app;

    public async ValueTask InitializeAsync()
    {
        string boot = Directory.CreateDirectory(Path.Combine(_root, "boot")).FullName;
        string bundle = Directory.CreateDirectory(Path.Combine(_root, "linux-boot")).FullName;
        Directory.CreateDirectory(Path.Combine(boot, "x64"));
        await File.WriteAllTextAsync(Path.Combine(boot, "x64", "bootmgfw.efi"), "boot manager");
        await File.WriteAllBytesAsync(Path.Combine(bundle, "vmlinuz"), _kernel);
        await File.WriteAllTextAsync(Path.Combine(bundle, "ddt-linux-boot.json"), "{}");

        _site.Current = TestBundle.Of(bundle, "vmlinuz") with { Keymap = "de", Changed = _changed.Add };

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseRouting();
        _app.UseBootListenerIsolation(BootPort, Loopback.Map());
        _app.MapBootFiles(new BootFiles(new BootFileResolver(boot), _site, BootPort, new BootConnections(_clock), _clock));

        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        Directory.Delete(_root, recursive: true);
    }

    private Task<HttpContext> SendAsync(string method, string path) =>
        _app!.GetTestServer().SendAsync(context =>
        {
            context.Request.Method = method;
            context.Request.Path = path;
            context.Connection.LocalPort = BootPort;
            context.Connection.LocalIpAddress = IPAddress.Loopback;
            context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.9");
        });

    private static async Task<byte[]> BodyOf(HttpContext context)
    {
        using MemoryStream copy = new();
        await context.Response.Body.CopyToAsync(copy);

        return copy.ToArray();
    }

    [Fact]
    public async Task ServesTheKernelFromTheBundleWithItsLength()
    {
        HttpContext context = await SendAsync(HttpMethods.Get, "/boot/linux/vmlinuz");

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(_kernel.Length, context.Response.ContentLength);
        Assert.Equal(_kernel, await BodyOf(context));
    }

    // GRUB reads as many bytes as Content-Length says, and reads nothing from a chunked answer.
    [Fact]
    public async Task ServesTheGeneratedSiteArchiveWithItsLength()
    {
        HttpContext context = await SendAsync(HttpMethods.Get, "/boot/linux/site.cpio");
        byte[] expected = SiteArchive.Write(_site.Current!, _clock.GetUtcNow());

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(expected.Length, context.Response.ContentLength);
        Assert.Equal("application/octet-stream", context.Response.ContentType);
        Assert.False(context.Response.Headers.ContainsKey("Transfer-Encoding"));
        Assert.Equal(expected, await BodyOf(context));
    }

    [Fact]
    public async Task AnswersAHeadOfAGeneratedFileWithItsLengthAndNoContent()
    {
        HttpContext context = await SendAsync(HttpMethods.Head, "/boot/linux/site.cpio");

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(SiteArchive.Write(_site.Current!, _clock.GetUtcNow()).Length, context.Response.ContentLength);
        Assert.Empty(await BodyOf(context));
    }

    [Fact]
    public async Task ServesGrubsConfigurationNamingTheAddressTheRequestArrivedOn()
    {
        HttpContext context = await SendAsync(HttpMethods.Get, "/boot/grub/grub.cfg");
        string text = Encoding.ASCII.GetString(await BodyOf(context));

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(text.Length, context.Response.ContentLength);
        Assert.StartsWith("set timeout=0\nif linux (http,127.0.0.1:8080)/boot/linux/vmlinuz ", text, StringComparison.Ordinal);
    }

    // GRUB never asks twice on a connection. Firmware that loads Windows PE does, and its answers stay as they were.
    [Theory]
    [InlineData("/boot/linux/vmlinuz", StatusCodes.Status200OK, true)]
    [InlineData("/boot/linux/site.cpio", StatusCodes.Status200OK, true)]
    [InlineData("/boot/grub/grub.cfg", StatusCodes.Status200OK, true)]
    [InlineData("/boot/linux/base.img", StatusCodes.Status404NotFound, true)]
    [InlineData("/boot/x64/bootmgfw.efi", StatusCodes.Status200OK, false)]
    [InlineData("/boot/x64/boot.wim", StatusCodes.Status404NotFound, false)]
    [InlineData("/boot/grub/fonts/unicode.pf2", StatusCodes.Status404NotFound, false)]
    public async Task OnlyAnAnswerToWhatGrubLoadsTellsTheMachineThatTheConnectionCloses(string path, int status, bool closes)
    {
        HttpContext context = await SendAsync(HttpMethods.Get, path);

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal(closes ? "close" : string.Empty, context.Response.Headers.Connection.ToString());
    }

    [Theory]
    [InlineData("/boot/linux/ddt-linux-boot.json")]
    [InlineData("/boot/linux/base.img")]
    [InlineData("/boot/linux/../linux-boot/vmlinuz")]
    [InlineData("/boot/linux/revocations_sbat.efi")]
    [InlineData("/boot/grub/x86_64-efi/command.lst")]
    [InlineData("/boot/vmlinuz")]
    public async Task ServesNothingTheBundleDoesNotList(string path)
    {
        HttpContext context = await SendAsync(HttpMethods.Get, path);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    // The server read the file once and found it to be the one its description names. What lies there now under the
    // same name and with the same length may be anything, so it is not sent, and the server is told.
    [Fact]
    public async Task AFileThatWasReplacedAtTheSameSizeSinceItWasCheckedIsNotServed()
    {
        string kernel = Path.Combine(_root, "linux-boot", "vmlinuz");
        await File.WriteAllBytesAsync(kernel, new byte[_kernel.Length], TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(kernel, File.GetLastWriteTimeUtc(kernel) + TimeSpan.FromSeconds(2));

        HttpContext context = await SendAsync(HttpMethods.Get, "/boot/linux/vmlinuz");

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Empty(await BodyOf(context));
        Assert.Equal(["vmlinuz"], _changed);
    }

    [Fact]
    public async Task AFileThatWasRemovedSinceItWasCheckedIsReportedGone()
    {
        File.Delete(Path.Combine(_root, "linux-boot", "vmlinuz"));

        HttpContext context = await SendAsync(HttpMethods.Head, "/boot/linux/vmlinuz");

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal(["vmlinuz"], _changed);
    }

    // A boot image build leaves its files under builds with a marker beside them. The Linux folder has no such thing.
    [Fact]
    public async Task AMarkerInTheBundlesFolderLeadsNowhere()
    {
        string bundle = Path.Combine(_root, "linux-boot");
        string other = Directory.CreateDirectory(Path.Combine(bundle, "builds", "other")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(other, "vmlinuz"), new byte[_kernel.Length], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(bundle, "current"), "other", TestContext.Current.CancellationToken);

        HttpContext context = await SendAsync(HttpMethods.Get, "/boot/linux/vmlinuz");

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(_kernel, await BodyOf(context));
        Assert.Empty(_changed);
    }

    // Windows PE's files are not checked by anyone, and a new boot image replaces them at any time.
    [Fact]
    public async Task AFileOfTheBootDirectoryIsServedAsItIsNow()
    {
        string bootManager = Path.Combine(_root, "boot", "x64", "bootmgfw.efi");
        await File.WriteAllTextAsync(bootManager, "another boot manager", TestContext.Current.CancellationToken);

        HttpContext context = await SendAsync(HttpMethods.Get, "/boot/x64/bootmgfw.efi");

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("another boot manager", System.Text.Encoding.ASCII.GetString(await BodyOf(context)));
        Assert.Empty(_changed);
    }

    [Fact]
    public async Task ServesNothingOfLinuxOnceTheBundleIsGone()
    {
        _site.Current = null;

        foreach (string path in new[] { "/boot/linux/vmlinuz", "/boot/linux/site.cpio", "/boot/grub/grub.cfg" })
        {
            Assert.Equal(StatusCodes.Status404NotFound, (await SendAsync(HttpMethods.Get, path)).Response.StatusCode);
        }
    }
}

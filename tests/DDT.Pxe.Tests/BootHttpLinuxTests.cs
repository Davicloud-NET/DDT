// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
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

    private WebApplication? _app;

    public async ValueTask InitializeAsync()
    {
        string boot = Directory.CreateDirectory(Path.Combine(_root, "boot")).FullName;
        string bundle = Directory.CreateDirectory(Path.Combine(_root, "linux-boot")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(bundle, "vmlinuz"), _kernel);
        await File.WriteAllTextAsync(Path.Combine(bundle, "ddt-linux-boot.json"), "{}");

        _site.Current = new LinuxBootBundle(bundle, new[] { "vmlinuz" }.ToFrozenSet(StringComparer.Ordinal), new Uri("https://ddt-server.example.test:8443"), "root")
        {
            Keymap = "de",
        };

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseRouting();
        _app.UseBootListenerIsolation(BootPort, Loopback.Map());
        _app.MapBootFiles(new BootFiles(new BootFileResolver(boot), _site, BootPort, _clock));

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

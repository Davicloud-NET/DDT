using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Pxe.Tests;

// Mirrors the order in DDT.Host: routing, the gate, then authentication with a deny by default
// fallback policy, so a request the gate lets through would meet the same policy it does in production.
public sealed class BootHttpEndpointsTests : IAsyncLifetime
{
    private const int BootPort = 8080;
    private const int HttpsPort = 8443;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ddt-http-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _content = [.. Enumerable.Range(0, 5000).Select(value => (byte)value)];

    private WebApplication? _app;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "x64"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "x64", "bootmgfw.efi"), _content);

        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        builder.Services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        _app = builder.Build();
        _app.UseRouting();
        _app.UseBootListenerIsolation(BootPort, Loopback.Map());
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapBootFiles(new BootFileResolver(_root));
        _app.MapGet("/api/ping", () => "pong");
        _app.MapFallback(() => "spa").AllowAnonymous();

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

    private Task<HttpContext> SendAsync(
        string method,
        string path,
        int port = BootPort,
        IPAddress? localAddress = null,
        Action<HttpRequest>? configure = null) =>
        _app!.GetTestServer().SendAsync(context =>
        {
            context.Request.Method = method;
            context.Request.Path = path;
            context.Connection.LocalPort = port;
            context.Connection.LocalIpAddress = localAddress ?? IPAddress.Loopback;
            configure?.Invoke(context.Request);
        });

    private static async Task<byte[]> BodyOf(HttpContext context)
    {
        using MemoryStream copy = new();
        await context.Response.Body.CopyToAsync(copy);

        return copy.ToArray();
    }

    [Fact]
    public async Task AnswersTheHeadFirmwareSendsFirst()
    {
        HttpContext context = await SendAsync(HttpMethods.Head, "/boot/x64/bootmgfw.efi");

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(_content.Length, context.Response.ContentLength);
        Assert.Equal("application/efi", context.Response.ContentType);
        Assert.Equal("bytes", context.Response.Headers.AcceptRanges.ToString());
    }

    [Fact]
    public async Task ServesTheFileAnonymously()
    {
        HttpContext context = await SendAsync(HttpMethods.Get, "/boot/x64/bootmgfw.efi");

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(_content, await BodyOf(context));
    }

    [Fact]
    public async Task ResumesWithARangeAndRefusesOneFromAReplacedFile()
    {
        HttpContext head = await SendAsync(HttpMethods.Head, "/boot/x64/bootmgfw.efi");
        string entityTag = head.Response.Headers.ETag.ToString();

        HttpContext resumed = await SendAsync(HttpMethods.Get, "/boot/x64/bootmgfw.efi", configure: request =>
        {
            request.Headers.Range = "bytes=1000-";
            request.Headers.IfMatch = entityTag;
        });

        Assert.Equal(StatusCodes.Status206PartialContent, resumed.Response.StatusCode);
        Assert.Equal(_content[1000..], await BodyOf(resumed));

        HttpContext stale = await SendAsync(HttpMethods.Get, "/boot/x64/bootmgfw.efi", configure: request =>
        {
            request.Headers.Range = "bytes=1000-";
            request.Headers.IfMatch = "\"not-the-file\"";
        });

        Assert.Equal(StatusCodes.Status412PreconditionFailed, stale.Response.StatusCode);
    }

    [Theory]
    [InlineData("/api/ping")]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/boot")]
    [InlineData("/health")]
    public async Task AnswersNothingButBootFilesOnTheBootPort(string path)
    {
        HttpContext context = await SendAsync(HttpMethods.Get, path);

        // Not a redirect and not 401: firmware refuses the first and prompts for credentials on the second.
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task ServesNoBootFileOnTheHttpsPort()
    {
        HttpContext context = await SendAsync(HttpMethods.Get, "/boot/x64/bootmgfw.efi", port: HttpsPort);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task LeavesTheRestOfTheApplicationAloneOnTheHttpsPort()
    {
        HttpContext context = await SendAsync(HttpMethods.Get, "/", port: HttpsPort);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task ServesNothingOnAnAddressThatIsNotServed()
    {
        HttpContext context = await SendAsync(HttpMethods.Get, "/boot/x64/bootmgfw.efi", localAddress: IPAddress.Parse("10.0.4.144"));

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task ServesNothingOnTheBootPortWhenNoInterfaceIsServed()
    {
        // The host declares the boot endpoint even when no named interface was up at startup, so the gate
        // has to hold the port shut rather than let the application answer on it.
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        await using WebApplication app = builder.Build();
        app.UseRouting();
        app.UseBootListenerIsolation(BootPort, new NetworkInterfaceMap(string.Empty, [Loopback.Interface]));
        app.MapBootFiles(new BootFileResolver(_root));
        app.MapFallback(() => "spa");
        await app.StartAsync(TestContext.Current.CancellationToken);

        foreach (string path in new[] { "/boot/x64/bootmgfw.efi", "/" })
        {
            HttpContext context = await app.GetTestServer().SendAsync(request =>
            {
                request.Request.Method = HttpMethods.Get;
                request.Request.Path = path;
                request.Connection.LocalPort = BootPort;
                request.Connection.LocalIpAddress = IPAddress.Loopback;
            }, TestContext.Current.CancellationToken);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }
    }

    [Theory]
    [InlineData("/boot/x64/..%5C..%5Cappsettings.json")]
    [InlineData("/boot/..%5C..%5Csecret")]
    [InlineData("/boot/x64/bootmgfw.efi.")]
    public async Task RefusesTraversalAndAliases(string path)
    {
        HttpContext context = await SendAsync(HttpMethods.Get, Uri.UnescapeDataString(path));

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }
}

using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace DDT.Pxe;

public static class BootHttpEndpoints
{
    // Must run after routing, so the endpoint is known, and before static files, the SPA fallback and
    // authorization, so nothing else can answer on the anonymous cleartext port. The single comparison
    // closes both directions: only boot files on the boot port, and no boot files on any other port.
    // Gating on the path prefix instead would let "/boot" with nothing after it fall through to the
    // SPA fallback over plain HTTP.
    //
    // Unlike TFTP and ProxyDHCP, which see the arrival interface, this checks the destination address.
    // A Linux host accepts a packet for any of its addresses on any interface, so a client elsewhere
    // that routes to a served address still reaches the files, which are public by design.
    public static IApplicationBuilder UseBootListenerIsolation(
        this IApplicationBuilder app,
        int bootPort,
        NetworkInterfaceMap interfaces)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(interfaces);

        return app.Use(async (context, next) =>
        {
            bool onBootPort = context.Connection.LocalPort == bootPort;
            bool bootEndpoint = context.GetEndpoint()?.Metadata.GetMetadata<BootEndpointMarker>() is not null;

            if (onBootPort != bootEndpoint
                || (onBootPort && (context.Connection.LocalIpAddress is not { } local || !interfaces.IsServedAddress(local))))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;

                return;
            }

            await next(context).ConfigureAwait(false);
        });
    }

    // GET and HEAD both: UEFI HTTP Boot sends HEAD first, and a HEAD that matches no endpoint falls
    // through to the deny by default policy and becomes a redirect the firmware refuses to follow.
    public static IEndpointConventionBuilder MapBootFiles(this IEndpointRouteBuilder endpoints, BootFileResolver files)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(files);

        return endpoints
            .MapMethods(
                "/boot/{**path}",
                [HttpMethods.Get, HttpMethods.Head],
                (string? path, HttpContext context, ILoggerFactory loggerFactory) => Serve(files, path, context, loggerFactory))
            .WithMetadata(new BootEndpointMarker())
            .AllowAnonymous();
    }

    private static IResult Serve(BootFileResolver files, string? path, HttpContext context, ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger(typeof(BootHttpEndpoints));
        string client = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        if (path is null || !files.TryResolve(path, out FileInfo? file))
        {
            PxeLog.HttpBootNotFound(logger, context.Request.Method, context.Request.Path, client);

            return TypedResults.NotFound();
        }

        PxeLog.HttpBoot(logger, context.Request.Method, context.Request.Path, client);

        // Firmware resumes an interrupted download with If-Match when it holds an ETag, so the tag has
        // to be strong. Without one the If-Match is ignored and a range from a replaced image is
        // spliced onto the old one.
        EntityTagHeaderValue entityTag = new(
            string.Create(CultureInfo.InvariantCulture, $"\"{file.LastWriteTimeUtc.Ticks:x}-{file.Length:x}\""));

        return TypedResults.PhysicalFile(
            file.FullName,
            ContentTypeFor(file.Name),
            fileDownloadName: null,
            lastModified: file.LastWriteTimeUtc,
            entityTag: entityTag,
            enableRangeProcessing: true);
    }

    // EDK II compares the Content-Type exactly before falling back to the extension, and refuses an
    // image whose type it does not recognise.
    private static string ContentTypeFor(string fileName) =>
        fileName.EndsWith(".efi", StringComparison.OrdinalIgnoreCase) ? "application/efi" : "application/octet-stream";
}

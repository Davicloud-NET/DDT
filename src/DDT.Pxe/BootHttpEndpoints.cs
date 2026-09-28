// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace DDT.Pxe;

public static class BootHttpEndpoints
{
    // After routing and before static files, the SPA fallback and authorization. Comparing the endpoint, not a path
    // prefix, keeps boot files on the cleartext boot port and all else off it. The destination address decides, not the
    // arrival interface, so a client routed to a served address reaches files that are public by design.
    public static IApplicationBuilder UseBootListenerIsolation(
        this IApplicationBuilder app,
        int bootPort,
        NetworkInterfaceMap interfaces)
    {
        ArgumentNullException.ThrowIfNull(interfaces);

        return app.UseBootListenerIsolation(bootPort, () => interfaces);
    }

    // Interfaces is read on every request, so the gate follows the setup the listeners were last started with. Null
    // serves nothing.
    public static IApplicationBuilder UseBootListenerIsolation(
        this IApplicationBuilder app,
        int bootPort,
        Func<NetworkInterfaceMap?> interfaces)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(interfaces);

        return app.Use(async (context, next) =>
        {
            bool onBootPort = context.Connection.LocalPort == bootPort;
            bool bootEndpoint = context.GetEndpoint()?.Metadata.GetMetadata<BootEndpointMarker>() is not null;

            if (onBootPort != bootEndpoint
                || (onBootPort && (context.Connection.LocalIpAddress is not { } local || interfaces() is not { } served || !served.IsServedAddress(local))))
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

        // Firmware resumes an interrupted download with If-Match, so the ETag must be strong: a weak one is ignored,
        // and a range of a replaced image gets spliced onto the old one.
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

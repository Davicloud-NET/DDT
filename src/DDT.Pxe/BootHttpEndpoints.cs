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
    // Goes after routing and before static files, the SPA fallback and authorization. It compares the endpoint, not a
    // path prefix, to keep boot files on the cleartext boot port and everything else off it. The destination address
    // decides, not the arrival interface. A client routed to a served address reaches files that are public by design.
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

    // Maps both GET and HEAD. UEFI HTTP Boot sends HEAD first, and a HEAD that matches no endpoint falls through to the
    // deny by default policy. That turns it into a redirect the firmware refuses to follow.
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

        // Not PhysicalFile, which opens without FileShare.Delete: on Windows a new boot image then can't move this
        // one aside while firmware downloads it.
        FileStream content;

        try
        {
            content = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            PxeLog.HttpBootNotFound(logger, context.Request.Method, context.Request.Path, client);

            return TypedResults.NotFound();
        }

        PxeLog.HttpBoot(logger, context.Request.Method, context.Request.Path, client);

        // Firmware resumes an interrupted download with If-Match, so the ETag must be strong. A weak one is ignored,
        // and then a range of a replaced image gets spliced onto the old one. Time and length are of the opened file.
        DateTime written = File.GetLastWriteTimeUtc(content.SafeFileHandle);
        EntityTagHeaderValue entityTag = new(
            string.Create(CultureInfo.InvariantCulture, $"\"{written.Ticks:x}-{content.Length:x}\""));

        return TypedResults.File(
            content,
            ContentTypeFor(file.Name),
            fileDownloadName: null,
            lastModified: written,
            entityTag: entityTag,
            enableRangeProcessing: true);
    }

    // EDK II compares the Content-Type exactly before falling back to the extension, and refuses an
    // image whose type it does not recognise.
    private static string ContentTypeFor(string fileName) =>
        fileName.EndsWith(".efi", StringComparison.OrdinalIgnoreCase) ? "application/efi" : "application/octet-stream";
}

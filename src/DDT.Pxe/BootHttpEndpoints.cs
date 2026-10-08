// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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
    // serves nothing. With connections, a read that is cut off is remembered for the machine's next start.
    public static IApplicationBuilder UseBootListenerIsolation(
        this IApplicationBuilder app,
        int bootPort,
        Func<NetworkInterfaceMap?> interfaces,
        BootConnections? connections = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(interfaces);

        return app.Use(async (context, next) =>
        {
            bool onBootPort = context.Connection.LocalPort == bootPort;
            bool bootEndpoint = context.GetEndpoint()?.Metadata.GetMetadata<BootEndpointMarker>() is not null;

            // GRUB opens a connection per file and never asks on it twice, so none is kept open for a next request.
            if (onBootPort)
            {
                context.Response.Headers.Connection = "close";
            }

            if (onBootPort != bootEndpoint
                || (onBootPort && (context.Connection.LocalIpAddress is not { } local || interfaces() is not { } served || !served.IsServedAddress(local))))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;

                return;
            }

            if (onBootPort && connections is not null)
            {
                await ReadAsync(context, next, connections).ConfigureAwait(false);
            }
            else
            {
                await next(context).ConfigureAwait(false);
            }
        });
    }

    // A read that ends before its answer is whole leaves the machine without the file, and maybe the server with the
    // connection.
    private static async Task ReadAsync(HttpContext context, RequestDelegate next, BootConnections connections)
    {
        bool whole = false;

        try
        {
            await next(context).ConfigureAwait(false);
            whole = !context.RequestAborted.IsCancellationRequested;
        }
        finally
        {
            if (!whole)
            {
                connections.ReadFailed(ClientOf(context));
            }
        }
    }

    // The connection's own address where Kestrel serves, which no forwarded header changes.
    private static IPAddress ClientOf(HttpContext context) =>
        context.Features.Get<BootClient>()?.Address ?? Version4(context.Connection.RemoteIpAddress ?? IPAddress.None);

    // Maps both GET and HEAD. UEFI HTTP Boot sends HEAD first, and a HEAD that matches no endpoint falls through to the
    // deny by default policy. That turns it into a redirect the firmware refuses to follow.
    public static IEndpointConventionBuilder MapBootFiles(this IEndpointRouteBuilder endpoints, BootFiles files)
    {
        ArgumentNullException.ThrowIfNull(files);

        return endpoints.MapBootFiles(() => files);
    }

    // files is asked at every request, so the port serves what the listeners were last started with. Null serves nothing.
    public static IEndpointConventionBuilder MapBootFiles(this IEndpointRouteBuilder endpoints, Func<BootFiles?> files)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(files);

        return endpoints
            .MapMethods(
                "/boot/{**path}",
                [HttpMethods.Get, HttpMethods.Head],
                (string? path, HttpContext context, ILoggerFactory loggerFactory) => Serve(files(), path, context, loggerFactory))
            .WithMetadata(new BootEndpointMarker())
            .AllowAnonymous();
    }

    private static IResult Serve(BootFiles? files, string? path, HttpContext context, ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger(typeof(BootHttpEndpoints));
        string client = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        if (files is null || path is null || RequestOf(context) is not { } request || !files.TryFind(path, request, out BootContent? content))
        {
            return NotFound(logger, context, client, path);
        }

        if (content is MemoryBootContent generated)
        {
            PxeLog.HttpBoot(logger, context.Request.Method, context.Request.Path, client);

            // GRUB reads a file whose length it was told, so the answer carries Content-Length and is not chunked.
            return TypedResults.Bytes(generated.Bytes, ContentTypeFor(path));
        }

        return content is FileBootContent file ? ServeFile(file.File, logger, context, client) : NotFound(logger, context, client, path);
    }

    // The address the request arrived on is the one a generated file names as the server.
    private static BootRequest? RequestOf(HttpContext context) =>
        context.Connection.LocalIpAddress is { } local ? new BootRequest(Version4(local), ClientOf(context)) : null;

    private static IPAddress Version4(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static NotFound NotFound(ILogger logger, HttpContext context, string client, string? path)
    {
        if (BootFiles.Normalized(path) is { } name && BootProbes.IsProbe(name))
        {
            PxeLog.HttpBootProbe(logger, context.Request.Method, context.Request.Path, client);
        }
        else
        {
            PxeLog.HttpBootNotFound(logger, context.Request.Method, context.Request.Path, client);
        }

        return TypedResults.NotFound();
    }

    private static IResult ServeFile(FileInfo file, ILogger logger, HttpContext context, string client)
    {
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

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using DDT.Contracts.Images;
using DDT.Server.Authentication;
using DDT.Server.Images;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// A resumable upload: create or find a session, send the file in chunks at the offset the server holds, then
// complete it. Every answer to a chunk carries the committed offset in Upload-Offset, and the client continues
// from there.
public static class ImageUploadEndpoints
{
    private const string UploadOffsetHeader = "Upload-Offset";

    public static RouteGroupBuilder MapImageUploadEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/", CreateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Administrator);

        // The body is streamed to disk and never bound, so the CSRF filters run before any of it is read. The handler
        // refuses an oversized chunk by its Content-Length; the size limit is a second line of defence.
        group.MapPatch("/{id:guid}", AppendAsync)
            .RequireAuthorization(DdtPolicies.Administrator)
            .WithMetadata(new RequestSizeLimitAttribute(ImageUploadLimits.ChunkBytes + 1));

        group.MapPost("/{id:guid}/complete", CompleteAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete("/{id:guid}", DiscardAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Results<Created<ImageUploadSession>, Ok<ImageUploadSession>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        CreateImageUploadRequest request,
        ClaimsPrincipal user,
        ImageUploadSessions sessions,
        ImageStore store,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FileName)
            || request.FileName.Length > ImageUploadLimits.MaxFileNameLength
            || request.FileName.Any(char.IsControl))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["fileName"] = [$"The file name must have 1 to {ImageUploadLimits.MaxFileNameLength} characters and no control characters."],
            });
        }

        // Packages come with their library.
        if (request.Kind != UploadKind.Image)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["kind"] = ["This server takes only images."],
            });
        }

        if (request.Length <= 0 || request.Length > store.Volume().TotalSize)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["length"] = ["The file must not be empty and must fit on the server's store volume."],
            });
        }

        UploadCreation creation = await sessions
            .FindOrCreateAsync(request, Principals.UserId(user), cancellationToken)
            .ConfigureAwait(false);

        if (creation.Session is not { } session)
        {
            return TypedResults.Problem(
                title: $"The image store needs {Gigabytes(creation.RequiredBytes)} free for this upload but has {Gigabytes(creation.AvailableBytes)}. "
                    + "Free space on the server's store volume or discard unfinished uploads, then try again.",
                statusCode: StatusCodes.Status507InsufficientStorage);
        }

        return creation.Created
            ? TypedResults.Created($"/api/images/uploads/{session.Id:D}", session)
            : TypedResults.Ok(session);
    }

    private static async Task<Ok<IReadOnlyList<ImageUploadSession>>> ListAsync(ImageUploadSessions sessions, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sessions.ListOpenAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> AppendAsync(
        Guid id,
        HttpContext context,
        ImageUploadSessions sessions,
        CancellationToken cancellationToken)
    {
        HttpRequest request = context.Request;

        if (request.ContentLength is not { } length)
        {
            return TypedResults.Problem(
                title: "Send every chunk with a Content-Length header.",
                statusCode: StatusCodes.Status411LengthRequired);
        }

        if (length is <= 0 or > ImageUploadLimits.ChunkBytes)
        {
            return TypedResults.Problem(
                title: $"A chunk holds 1 to {ImageUploadLimits.ChunkBytes} bytes. Send the file in smaller chunks.",
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        if (!long.TryParse(request.Headers[UploadOffsetHeader], NumberStyles.None, CultureInfo.InvariantCulture, out long offset))
        {
            return TypedResults.Problem(
                title: "Send the position of the chunk in the file in the Upload-Offset header.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        UploadAppend result = await sessions.AppendAsync(id, offset, length, request.Body, cancellationToken).ConfigureAwait(false);

        if (result.Status == UploadAppendStatus.NotFound)
        {
            return TypedResults.NotFound();
        }

        context.Response.Headers[UploadOffsetHeader] = result.Offset.ToString(CultureInfo.InvariantCulture);

        return result.Status switch
        {
            UploadAppendStatus.Appended => TypedResults.NoContent(),
            UploadAppendStatus.BeyondLength => Refusal(
                "This chunk ends past the end of the file. Upload the file this session was created for.",
                StatusCodes.Status400BadRequest),
            UploadAppendStatus.Completed => Refusal("This upload is complete. Nothing more needs to be sent.", StatusCodes.Status409Conflict),
            UploadAppendStatus.Busy => RetryLater(context, "Another request is using this upload. Wait a few seconds, then send the chunk again."),
            UploadAppendStatus.OffsetMismatch => Refusal(
                "The server holds a different part of this file. Continue from the offset in the Upload-Offset header.",
                StatusCodes.Status409Conflict),
            UploadAppendStatus.Restarted => Refusal(
                "The server lost part of this upload. Send the file again from the start.",
                StatusCodes.Status409Conflict),
            UploadAppendStatus.CutOff => Refusal("The chunk ended before all of it arrived. Send it again.", StatusCodes.Status400BadRequest),
            UploadAppendStatus.Stalled => Refusal(
                "The chunk stopped arriving for too long. Send it again.",
                StatusCodes.Status408RequestTimeout),
            UploadAppendStatus.DiskFull => Refusal(
                "The image store is full. Free space on the server's store volume, then continue the upload.",
                StatusCodes.Status507InsufficientStorage),
            _ => throw new UnreachableException(),
        };
    }

    private static async Task<Results<
        Created<IReadOnlyList<ImageSummary>>,
        Ok<IReadOnlyList<ImageSummary>>,
        NotFound,
        ProblemHttpResult,
        StatusCodeHttpResult>> CompleteAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        ImageUploadCompleter completer,
        CancellationToken cancellationToken)
    {
        UploadCompletion completion;

        try
        {
            completion = await completer
                .CompleteAsync(id, Principals.UserId(user), user.Identity?.Name, context.Connection.RemoteIpAddress?.ToString(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client has gone. The work goes on, and its stored result answers the client's next attempt.
            return TypedResults.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }

        switch (completion.Status)
        {
            case UploadCompletionStatus.Added:
                return TypedResults.Created((string?)null, completion.Images);
            case UploadCompletionStatus.Existing:
                return TypedResults.Ok(completion.Images);
            case UploadCompletionStatus.NotFound:
                return TypedResults.NotFound();
            case UploadCompletionStatus.Busy:
                return RetryLater(context, "This upload is being checked or written to. Ask again in a few seconds.");
            case UploadCompletionStatus.Incomplete:
                context.Response.Headers[UploadOffsetHeader] = completion.Offset.ToString(CultureInfo.InvariantCulture);

                return Refusal(
                    "Not all of the file has arrived. Continue the upload from the offset in the Upload-Offset header.",
                    StatusCodes.Status409Conflict);
            case UploadCompletionStatus.Refused:
                return Refusal(completion.Refusal!, StatusCodes.Status422UnprocessableEntity);
            case UploadCompletionStatus.Failed:
                return Refusal(
                    "The upload could not be added to the library. Look at the server log, then complete the upload again.",
                    StatusCodes.Status500InternalServerError);
            case UploadCompletionStatus.Stopping:
                return Refusal("The server is stopping. Complete the upload again once it is back.", StatusCodes.Status503ServiceUnavailable);
            default:
                throw new UnreachableException();
        }
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DiscardAsync(
        Guid id,
        HttpContext context,
        ImageUploadSessions sessions,
        CancellationToken cancellationToken) =>
        await sessions.DiscardAsync(id, cancellationToken).ConfigureAwait(false) switch
        {
            UploadDiscardStatus.Discarded => TypedResults.NoContent(),
            UploadDiscardStatus.NotFound => TypedResults.NotFound(),
            UploadDiscardStatus.Busy => RetryLater(context, "This upload is in use. Try again in a few seconds."),
            _ => throw new UnreachableException(),
        };

    private static ProblemHttpResult Refusal(string title, int statusCode) => TypedResults.Problem(title: title, statusCode: statusCode);

    private static ProblemHttpResult RetryLater(HttpContext context, string title)
    {
        context.Response.Headers.RetryAfter = ImageUploadLimits.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        return TypedResults.Problem(title: title, statusCode: StatusCodes.Status409Conflict);
    }

    // Binary gigabytes, labelled GB like every other size the operator sees.
    private static string Gigabytes(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024 * 1024):0.0} GB");
}

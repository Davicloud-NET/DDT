// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using DDT.Contracts.Images;
using DDT.Contracts.Messages;
using DDT.Contracts.Packages;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// A resumable upload: a session, the file in chunks at the offset the server holds, then completion. Every answer to a
// chunk carries the committed offset in Upload-Offset, where the client continues.
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
            return ServerProblems.Validation("fileName", ServerMessages.UploadFileName.With("max", ImageUploadLimits.MaxFileNameLength));
        }

        if (!Enum.IsDefined(request.Kind))
        {
            return ServerProblems.Validation("kind", ServerMessages.UploadKind.With());
        }

        if (request.Length <= 0 || request.Length > store.Volume().TotalSize)
        {
            return ServerProblems.Validation("length", ServerMessages.UploadLength.With());
        }

        UploadCreation creation = await sessions
            .FindOrCreateAsync(request, Principals.UserId(user), cancellationToken)
            .ConfigureAwait(false);

        if (creation.Session is not { } session)
        {
            return ServerProblems.Problem(
                ServerMessages.UploadNoSpace.With("required", Gigabytes(creation.RequiredBytes), "available", Gigabytes(creation.AvailableBytes)),
                StatusCodes.Status507InsufficientStorage);
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
            return ServerProblems.Problem(ServerMessages.UploadContentLength.With(), StatusCodes.Status411LengthRequired);
        }

        if (length is <= 0 or > ImageUploadLimits.ChunkBytes)
        {
            return ServerProblems.Problem(ServerMessages.UploadChunkSize.With("max", ImageUploadLimits.ChunkBytes), StatusCodes.Status413PayloadTooLarge);
        }

        if (!long.TryParse(request.Headers[UploadOffsetHeader], NumberStyles.None, CultureInfo.InvariantCulture, out long offset))
        {
            return ServerProblems.Problem(ServerMessages.UploadOffsetHeader.With(), StatusCodes.Status400BadRequest);
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
            UploadAppendStatus.BeyondLength => Refusal(ServerMessages.UploadBeyondLength, StatusCodes.Status400BadRequest),
            UploadAppendStatus.Completed => Refusal(ServerMessages.UploadComplete, StatusCodes.Status409Conflict),
            UploadAppendStatus.Busy => RetryLater(context, ServerMessages.UploadChunkBusy),
            UploadAppendStatus.OffsetMismatch => Refusal(ServerMessages.UploadOffsetMismatch, StatusCodes.Status409Conflict),
            UploadAppendStatus.Restarted => Refusal(ServerMessages.UploadRestarted, StatusCodes.Status409Conflict),
            UploadAppendStatus.CutOff => Refusal(ServerMessages.UploadCutOff, StatusCodes.Status400BadRequest),
            UploadAppendStatus.Stalled => Refusal(ServerMessages.UploadStalled, StatusCodes.Status408RequestTimeout),
            UploadAppendStatus.DiskFull => Refusal(ServerMessages.UploadDiskFull, StatusCodes.Status507InsufficientStorage),
            _ => throw new UnreachableException(),
        };
    }

    private static async Task<Results<
        Created<IReadOnlyList<ImageSummary>>,
        Ok<IReadOnlyList<ImageSummary>>,
        Created<PackageSummary>,
        Ok<PackageSummary>,
        ProblemHttpResult,
        StatusCodeHttpResult>> CompleteAsync(
        Guid id,
        HttpContext context,
        ImageUploadCompleter completer,
        CancellationToken cancellationToken)
    {
        UploadCompletion completion;

        try
        {
            completion = await completer
                .CompleteAsync(id, Actor.Of(context), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client has gone. The work goes on, and its stored result answers the client's next attempt.
            return TypedResults.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }

        switch (completion.Status)
        {
            case UploadCompletionStatus.Added when completion.Package is { } package:
                return TypedResults.Created((string?)null, package);
            case UploadCompletionStatus.Added:
                return TypedResults.Created((string?)null, completion.Images);
            case UploadCompletionStatus.Existing when completion.Package is { } package:
                return TypedResults.Ok(package);
            case UploadCompletionStatus.Existing:
                return TypedResults.Ok(completion.Images);
            case UploadCompletionStatus.NotFound:
                return Refusal(ServerMessages.UploadGone, StatusCodes.Status404NotFound);
            case UploadCompletionStatus.Busy:
                return RetryLater(context, ServerMessages.UploadBeingChecked);
            case UploadCompletionStatus.Incomplete:
                context.Response.Headers[UploadOffsetHeader] = completion.Offset.ToString(CultureInfo.InvariantCulture);

                return Refusal(ServerMessages.UploadIncomplete, StatusCodes.Status409Conflict);
            case UploadCompletionStatus.Refused:
                return ServerProblems.Problem(completion.Refusal!, StatusCodes.Status422UnprocessableEntity);
            case UploadCompletionStatus.Kept:
                return ServerProblems.Problem(
                    completion.Refusal!,
                    completion.Refusal!.Code == ServerMessages.UploadConversionOutOfSpace.Code
                        ? StatusCodes.Status507InsufficientStorage
                        : StatusCodes.Status422UnprocessableEntity);
            case UploadCompletionStatus.Failed:
                return Refusal(ServerMessages.UploadFailed, StatusCodes.Status500InternalServerError);
            case UploadCompletionStatus.Stopping:
                return Refusal(ServerMessages.UploadServerStopping, StatusCodes.Status503ServiceUnavailable);
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
            UploadDiscardStatus.Busy => RetryLater(context, ServerMessages.UploadInUse),
            _ => throw new UnreachableException(),
        };

    private static ProblemHttpResult Refusal(MessageTemplate message, int statusCode) => ServerProblems.Problem(message.With(), statusCode);

    private static ProblemHttpResult RetryLater(HttpContext context, MessageTemplate message)
    {
        context.Response.Headers.RetryAfter = ImageUploadLimits.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        return ServerProblems.Problem(message.With(), StatusCodes.Status409Conflict);
    }

    // Binary gigabytes, labelled GB like every other size the operator sees.
    private static string Gigabytes(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024 * 1024):0.0} GB");
}

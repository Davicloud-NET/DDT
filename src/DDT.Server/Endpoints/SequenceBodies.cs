// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DDT.Contracts.Messages;
using DDT.Server.Sequences;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DDT.Server.Endpoints;

// Bodies that hold a sequence document are read here rather than bound: the serializer throws NotSupportedException
// for a step without "kind", which binding does not turn into a 400.
internal static class SequenceBodies
{
    public static async Task<(T? Body, ProblemHttpResult? Refusal)> ReadAsync<T>(
        HttpRequest request,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!request.HasJsonContentType())
        {
            return (null, ServerProblems.Problem(ServerMessages.SequenceSendJson.With(), StatusCodes.Status415UnsupportedMediaType));
        }

        if (request.ContentLength > SequenceLimits.MaxRequestBytes)
        {
            return (null, TooLarge());
        }

        try
        {
            T? body = await JsonSerializer.DeserializeAsync(request.Body, typeInfo, cancellationToken).ConfigureAwait(false);

            return body is null ? (null, Malformed("The body is empty.")) : (body, null);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return (null, Malformed(exception.Message));
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, TooLarge());
        }
    }

    // The detail is the reader's own English, for whoever writes a client.
    public static ProblemHttpResult Malformed(string detail) =>
        ServerProblems.Problem(ServerMessages.SequenceUnreadable.With(), StatusCodes.Status400BadRequest, detail: detail);

    private static ProblemHttpResult TooLarge() =>
        ServerProblems.Problem(
            ServerMessages.SequenceRequestTooLarge.With("max", SequenceLimits.MaxRequestBytes / 1024),
            StatusCodes.Status413PayloadTooLarge);
}

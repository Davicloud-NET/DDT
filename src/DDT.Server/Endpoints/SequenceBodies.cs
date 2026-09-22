// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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
            return (null, TypedResults.Problem(title: "Send the sequence as JSON.", statusCode: StatusCodes.Status415UnsupportedMediaType));
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

    public static ProblemHttpResult Malformed(string detail) =>
        TypedResults.Problem(
            title: "The sequence is not a document DDT can read. Every step needs an id and a kind this version of DDT knows.",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult TooLarge() =>
        TypedResults.Problem(
            title: $"A sequence request can have at most {SequenceLimits.MaxRequestBytes / 1024} KiB.",
            statusCode: StatusCodes.Status413PayloadTooLarge);
}

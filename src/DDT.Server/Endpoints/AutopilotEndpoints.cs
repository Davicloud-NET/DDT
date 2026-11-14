// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Server.Authentication;
using DDT.Server.Autopilot;
using DDT.Server.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

// The machines' Autopilot hashes: an upload again, and Intune's CSV for one machine or a selection. Operators, as for
// other work on machines.
public static class AutopilotEndpoints
{
    public const string FileName = "autopilot.csv";

    public static RouteGroupBuilder MapAutopilotEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/{id:guid}/autopilot/upload", UploadAsync).RequireAuthorization(DdtPolicies.Operator);
        group.MapGet("/{id:guid}/autopilot.csv", OneAsync).RequireAuthorization(DdtPolicies.Operator);
        group.MapPost("/autopilot.csv", SelectionAsync).RequireAuthorization(DdtPolicies.Operator);

        return group;
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> UploadAsync(
        Guid id,
        DdtDbContext database,
        AutopilotActions actions,
        CancellationToken cancellationToken)
    {
        if (!await database.Machines.AnyAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.NotFound();
        }

        ServerMessage? refusal = await actions.RequestUploadAsync(id, cancellationToken).ConfigureAwait(false);

        return refusal is null ? TypedResults.NoContent() : ServerProblems.Problem(refusal, StatusCodes.Status409Conflict);
    }

    private static async Task<Results<FileContentHttpResult, NotFound, ProblemHttpResult>> OneAsync(
        Guid id,
        DdtDbContext database,
        AutopilotActions actions,
        CancellationToken cancellationToken)
    {
        if (!await database.Machines.AnyAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false))
        {
            return TypedResults.NotFound();
        }

        return Csv(await actions.CsvAsync([id], cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Results<FileContentHttpResult, NotFound, ProblemHttpResult>> SelectionAsync(
        AutopilotCsvRequest request,
        AutopilotActions actions,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid> ids = [.. (request?.MachineIds ?? []).Distinct()];

        return Csv(await actions.CsvAsync(ids, cancellationToken).ConfigureAwait(false));
    }

    private static Results<FileContentHttpResult, NotFound, ProblemHttpResult> Csv((byte[]? Csv, ServerMessage? Refusal) answer) =>
        answer.Csv is { } csv
            ? TypedResults.File(csv, AutopilotCsv.ContentType, FileName)
            : ServerProblems.Problem(answer.Refusal ?? ServerMessages.AutopilotNoHashes.With(), StatusCodes.Status409Conflict);
}

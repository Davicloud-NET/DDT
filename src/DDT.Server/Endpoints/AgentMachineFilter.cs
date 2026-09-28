// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Endpoints;

// Loads the machine an agent's route names, tracked, once the token is that machine's own, in its current generation.
// Returns 403 for another machine's token, 404 for a machine that doesn't exist, and 401 for a token from before it
// started over.
internal sealed class AgentMachineFilter : IEndpointFilter
{
    private static readonly object s_machineKey = new();

    public static Machine MachineOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items[s_machineKey] as Machine
            ?? throw new InvalidOperationException("The endpoint does not run AgentMachineFilter.");
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        HttpContext http = context.HttpContext;

        // A body that failed to bind already set 400 and skips the handler. That answer stands, without a database
        // read.
        if (http.Response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            return await next(context).ConfigureAwait(false);
        }

        Guid id = Guid.Parse(http.Request.RouteValues["id"]?.ToString() ?? string.Empty);

        if (!Principals.IsMachine(http.User, id))
        {
            return TypedResults.Forbid(authenticationSchemes: [DdtAuthenticationSchemes.Machine]);
        }

        DdtDbContext database = http.RequestServices.GetRequiredService<DdtDbContext>();
        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == id, http.RequestAborted).ConfigureAwait(false);

        if (machine is null)
        {
            return TypedResults.NotFound();
        }

        if (!Principals.HoldsCurrentGeneration(http.User, machine))
        {
            return TypedResults.Unauthorized();
        }

        http.Items[s_machineKey] = machine;

        return await next(context).ConfigureAwait(false);
    }
}

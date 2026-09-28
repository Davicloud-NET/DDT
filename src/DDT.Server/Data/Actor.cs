// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Endpoints;
using Microsoft.AspNetCore.Http;

namespace DDT.Server.Data;

// Who made a change, as audit rows and the UpdatedBy columns record it.
public sealed record Actor(Guid? UserId, string? Name, string? Address, Guid? MachineId = null)
{
    public static Actor Console { get; } = new(null, "console", null);

    public static Actor Configuration { get; } = new(null, "configuration", null);

    // DDT acting on its own, such as when it fails a run because the agent went silent.
    public static Actor Nobody { get; } = new(null, null, null);

    // Uses the user name alone. AuditInterceptor adds an API token's name to the audit rows.
    public static Actor Of(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new(Principals.UserId(context.User), context.User.Identity?.Name, context.Connection.RemoteIpAddress?.ToString());
    }

    public static Actor OfMachine(Guid machineId, string? address) => new(null, null, address, machineId);
}

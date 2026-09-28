// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Server.Endpoints;
using Microsoft.AspNetCore.Http;

namespace DDT.Server.Security;

// Keeps API tokens off what changes the account itself, such as its password, second factor or tokens, so a leaked
// token can neither lock its owner out nor mint itself a successor.
public sealed class SessionOnlyEndpointFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        return Principals.ApiTokenId(context.HttpContext.User) is null
            ? next(context)
            : ValueTask.FromResult<object?>(ServerProblems.Problem(ServerMessages.AccountApiTokenCannotChange.With(), StatusCodes.Status403Forbidden));
    }
}

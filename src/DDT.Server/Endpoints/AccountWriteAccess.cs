// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Server.Data;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Endpoints;

// Who may change the accounts, bound with [AsParameters]: a person signed in on the web who entered their password again
// in the last five minutes. A token of a script proves nobody is there.
internal sealed record AccountWriteAccess(HttpContext Context, ReauthenticationTokens Reauthentication, UserManager<DdtUser> Users)
{
    public Actor Actor => Actor.Of(Context);

    // Null when the request may write.
    public async Task<ProblemHttpResult?> RefusedAsync()
    {
        if (Principals.ApiTokenId(Context.User) is not null)
        {
            return ServerProblems.Problem(ServerMessages.StepAccountApiToken.With(), StatusCodes.Status403Forbidden);
        }

        if (!await Reauthentication.ValidAsync(Context, Context.User, Users).ConfigureAwait(false))
        {
            // Fields as the settings name what needs the proof, so a page asks for the password the same way.
            return ServerProblems.Problem(
                ServerMessages.StepAccountReauthenticate.With(),
                StatusCodes.Status403Forbidden,
                new Dictionary<string, object?> { ["fields"] = new[] { "account" } });
        }

        return null;
    }
}

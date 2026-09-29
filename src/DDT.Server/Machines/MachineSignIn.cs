// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using IdentitySignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace DDT.Server.Machines;

// A sign-in at a waiting machine, checked against the same accounts, lockout and directory as the web sign-in. If the
// user may deploy, the sign-in authorizes the machine.
internal sealed class MachineSignIn(
    DdtDbContext database,
    CredentialVerifier credentials,
    UserManager<DdtUser> users,
    UserActivity activity,
    SignInApprovals approvals,
    ILoggerFactory loggerFactory)
{
    public async Task<MachineSignInOutcome> SignInAsync(
        Guid machineId,
        AgentSignInRequest request,
        ClaimsPrincipal user,
        string address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Loaded before the credentials are checked, which takes a noticeable moment, so the concurrency tokens cover
        // that time. A registration that starts the machine over in the meantime mustn't get this approval.
        Machine? machine = await database.Machines.FirstOrDefaultAsync(m => m.Id == machineId, cancellationToken).ConfigureAwait(false);

        if (machine is null)
        {
            return MachineSignInOutcome.NotFound;
        }

        if (!Principals.HoldsCurrentGeneration(user, machine))
        {
            return MachineSignInOutcome.StartedOver;
        }

        if (machine.State != MachineState.Pending || machine.SignedInByUserId is not null)
        {
            return MachineSignInOutcome.Answered(AgentSignInStatus.AlreadyDecided);
        }

        (AgentSignInStatus status, Actor? signer) = await VerifyAsync(request, machineId, address, cancellationToken).ConfigureAwait(false);

        return signer is null
            ? MachineSignInOutcome.Answered(status)
            : await approvals.SignedInAsync(machine, signer, user, cancellationToken).ConfigureAwait(false);
    }

    // Signer is who signed in, if they may deploy.
    private async Task<(AgentSignInStatus Status, Actor? Signer)> VerifyAsync(
        AgentSignInRequest request,
        Guid machineId,
        string address,
        CancellationToken cancellationToken)
    {
        ILogger logger = loggerFactory.CreateLogger(typeof(AgentEndpoints));

        (IdentitySignInResult result, DdtUser? account) = await credentials
            .VerifyAsync(request.UserName, request.Password, request.TwoFactorCode, cancellationToken)
            .ConfigureAwait(false);

        if (result.RequiresTwoFactor)
        {
            return (AgentSignInStatus.RequiresTwoFactor, null);
        }

        if (result.IsLockedOut)
        {
            AuthLog.MachineSignInLockedOut(logger, machineId, request.UserName, address);
            await AuthEndpoints.LockedOutAsync(request.UserName, users, activity, cancellationToken).ConfigureAwait(false);

            return (AgentSignInStatus.LockedOut, null);
        }

        if (result is NoRoleSignInResult)
        {
            AuthLog.MachineSignInNotPermitted(logger, request.UserName, machineId, address);

            return (AgentSignInStatus.NotPermitted, null);
        }

        if (!result.Succeeded || account is null)
        {
            AuthLog.MachineSignInFailed(logger, machineId, request.UserName, address);

            return (AgentSignInStatus.Failed, null);
        }

        string userName = account.UserName ?? request.UserName;

        if (!await MayDeployAsync(account).ConfigureAwait(false))
        {
            AuthLog.MachineSignInNotPermitted(logger, userName, machineId, address);

            return (AgentSignInStatus.NotPermitted, null);
        }

        return (AgentSignInStatus.Succeeded, new Actor(account.Id, userName, address, machineId));
    }

    // A password an administrator was shown authorizes nothing until the user has set their own. The web sign-in works
    // the same way.
    private async Task<bool> MayDeployAsync(DdtUser account) =>
        (await users.IsInRoleAsync(account, DdtRoleNames.Operator).ConfigureAwait(false)
            || await users.IsInRoleAsync(account, DdtRoleNames.Administrator).ConfigureAwait(false))
        && !(await users.GetClaimsAsync(account).ConfigureAwait(false)).Any(claim => claim.Type == DdtClaimTypes.MustChangePassword);
}

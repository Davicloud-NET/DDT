// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using DDT.Contracts.Messages;
using DDT.Contracts.Users;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Users;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// Accounts and their roles, for administrators.
public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/", CreateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPatch("/{id:guid}", UpdateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/{id:guid}/disable", DisableAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/{id:guid}/enable", EnableAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/{id:guid}/reset-password", ResetPasswordAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/{id:guid}/reset-two-factor", ResetTwoFactorAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<UserView>>> ListAsync(UserViews views, CancellationToken cancellationToken) =>
        TypedResults.Ok(await views.ListAsync(cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Created<CreatedUser>, ValidationProblem>> CreateAsync(
        CreateUserRequest request,
        HttpContext context,
        UserAccounts accounts,
        CancellationToken cancellationToken) =>
        await accounts.CreateAsync(request, Actor.Of(context), cancellationToken).ConfigureAwait(false) switch
        {
            { View: { } view, Password: { } password } => TypedResults.Created($"/api/users/{view.Id:D}", new CreatedUser(view, password)),
            { Failure: { } failure } => failure.ToValidationProblem(UserField),
            { Problems: { } problems } => problems.ToResult(),
            _ => throw new UnreachableException(),
        };

    private static async Task<Results<Ok<UserView>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        HttpContext context,
        UserAccounts accounts,
        CancellationToken cancellationToken) =>
        Answer(await accounts.UpdateAsync(id, request, Actor.Of(context), cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Ok<UserView>, NotFound, ValidationProblem, ProblemHttpResult>> DisableAsync(
        Guid id,
        HttpContext context,
        UserAccounts accounts,
        CancellationToken cancellationToken) =>
        Answer(await accounts.DisableAsync(id, Actor.Of(context), cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Ok<UserView>, NotFound, ValidationProblem, ProblemHttpResult>> EnableAsync(
        Guid id,
        HttpContext context,
        UserAccounts accounts,
        CancellationToken cancellationToken) =>
        Answer(await accounts.EnableAsync(id, Actor.Of(context), cancellationToken).ConfigureAwait(false));

    private static async Task<Results<Ok<OneTimePassword>, NotFound, ProblemHttpResult>> ResetPasswordAsync(
        Guid id,
        HttpContext context,
        UserAccounts accounts,
        CancellationToken cancellationToken) =>
        await accounts.ResetPasswordAsync(id, Actor.Of(context), cancellationToken).ConfigureAwait(false) switch
        {
            { Status: UserChangeStatus.Done, Password: { } password } => TypedResults.Ok(new OneTimePassword(password)),
            { Status: UserChangeStatus.NotFound } => TypedResults.NotFound(),
            var refused => Refusal(refused),
        };

    private static async Task<Results<Ok<UserView>, NotFound, ValidationProblem, ProblemHttpResult>> ResetTwoFactorAsync(
        Guid id,
        HttpContext context,
        UserAccounts accounts,
        CancellationToken cancellationToken) =>
        Answer(await accounts.ResetTwoFactorAsync(id, Actor.Of(context), cancellationToken).ConfigureAwait(false));

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        HttpContext context,
        UserAccounts accounts,
        CancellationToken cancellationToken) =>
        await accounts.DeleteAsync(id, Actor.Of(context), cancellationToken).ConfigureAwait(false) switch
        {
            { Status: UserChangeStatus.Done } => TypedResults.NoContent(),
            { Status: UserChangeStatus.NotFound } => TypedResults.NotFound(),
            var refused => Refusal(refused),
        };

    private static Results<Ok<UserView>, NotFound, ValidationProblem, ProblemHttpResult> Answer(UserChange change) => change switch
    {
        { Status: UserChangeStatus.Done, View: { } view } => TypedResults.Ok(view),
        { Status: UserChangeStatus.NotFound } => TypedResults.NotFound(),
        { Status: UserChangeStatus.Invalid, Problems: { } problems } => problems.ToResult(),
        _ => Refusal(change),
    };

    private static ProblemHttpResult Refusal(UserChange change) => change switch
    {
        { Refusal: { } refusal } => ServerProblems.Problem(refusal, StatusCodes.Status409Conflict),
        { Failure: { } failure } => NotSaved(failure),
        _ => throw new UnreachableException(),
    };

    // Several errors are joined into one title, which has no code.
    private static ProblemHttpResult NotSaved(IdentityResult result)
    {
        if (result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
        {
            return ServerProblems.Problem(ServerMessages.UserChangedWhileSaving.With(), StatusCodes.Status409Conflict);
        }

        return result.Errors.Count() == 1
            ? ServerProblems.Problem(AuthEndpoints.MessageOf(result.Errors.First()), StatusCodes.Status400BadRequest)
            : TypedResults.Problem(title: string.Join(" ", result.Errors.Select(error => error.Description)), statusCode: StatusCodes.Status400BadRequest);
    }

    private static string UserField(IdentityError error) =>
        error.Code is nameof(IdentityErrorDescriber.DuplicateUserName) or nameof(IdentityErrorDescriber.InvalidUserName) ? "userName" : error.Code;
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Users;
using DDT.Server.Authentication;
using DDT.Server.Ldap;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace DDT.Server.Endpoints;

// The directory sign-in as the Users page shows it: the group map, groups to choose from by name, and what a sign-in
// would give a user. Everything is read with the bind account, as a sign-in reads it, and nothing is changed. The map
// is read-only here until the settings page takes it over.
public static class DirectoryEndpoints
{
    public const int DefaultGroupLimit = 20;

    public const int MaxGroupLimit = 100;

    public static RouteGroupBuilder MapDirectoryEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ReadAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapGet("/groups", SearchGroupsAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/check", CheckAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    // The names are the directory's, so a map entry for a group the directory does not have shows without one. The page
    // still shows the map while the directory cannot be asked.
    private static async Task<Ok<DirectoryView>> ReadAsync(
        IOptions<LdapOptions> options,
        DirectorySignInService directory,
        ILdapAuthenticator authenticator,
        CancellationToken cancellationToken)
    {
        LdapOptions ldap = options.Value;
        IReadOnlyDictionary<string, string?> names = new Dictionary<string, string?>();

        if (directory.Configured && ldap.GroupRoleMap.Count > 0)
        {
            try
            {
                names = await authenticator.GroupNamesAsync(ldap.GroupRoleMap.Keys, cancellationToken).ConfigureAwait(false);
            }
            catch (LdapUnavailableException)
            {
            }
        }

        return TypedResults.Ok(new DirectoryView(
            ldap.Enabled,
            ldap.Host,
            ldap.BaseDn,
            [
                .. ldap.GroupRoleMap
                    .Select(entry => new DirectoryGroupMapping(entry.Key, names.GetValueOrDefault(entry.Key), DdtRoleNames.Canonical(entry.Value) ?? entry.Value))
                    .OrderBy(mapping => DdtRoleNames.Rank(mapping.Role))
                    .ThenBy(mapping => mapping.Name ?? mapping.Group, StringComparer.OrdinalIgnoreCase),
            ]));
    }

    private static async Task<Results<Ok<IReadOnlyList<DirectoryGroup>>, ProblemHttpResult>> SearchGroupsAsync(
        string? query,
        int? limit,
        DirectorySignInService directory,
        ILdapAuthenticator authenticator,
        CancellationToken cancellationToken)
    {
        if (Unconfigured(directory) is { } refusal)
        {
            return refusal;
        }

        try
        {
            IReadOnlyList<LdapGroup> groups = await authenticator
                .SearchGroupsAsync(query ?? string.Empty, Math.Clamp(limit ?? DefaultGroupLimit, 1, MaxGroupLimit), cancellationToken)
                .ConfigureAwait(false);

            return TypedResults.Ok<IReadOnlyList<DirectoryGroup>>(
                [.. groups.Select(group => new DirectoryGroup(group.DistinguishedName, group.Name, group.Description))]);
        }
        catch (LdapUnavailableException exception)
        {
            return Unavailable(exception);
        }
    }

    private static async Task<Results<Ok<DirectoryCheck>, ValidationProblem, ProblemHttpResult>> CheckAsync(
        DirectoryCheckRequest request,
        DirectorySignInService directory,
        CancellationToken cancellationToken)
    {
        string? userName = request.UserName?.Trim();

        if (string.IsNullOrEmpty(userName))
        {
            return ServerProblems.Validation("userName", ServerMessages.DirectoryEnterUserName.With());
        }

        if (Unconfigured(directory) is { } refusal)
        {
            return refusal;
        }

        try
        {
            return TypedResults.Ok(await directory.CheckAsync(userName, cancellationToken).ConfigureAwait(false));
        }
        catch (LdapUnavailableException exception)
        {
            return Unavailable(exception);
        }
    }

    private static ProblemHttpResult? Unconfigured(DirectorySignInService directory) =>
        directory.Enabled
            ? directory.Configured
                ? null
                : Conflict(ServerMessages.DirectoryIncomplete.With())
            : Conflict(ServerMessages.DirectoryOff.With());

    // DDT stands between the page and the directory here, so a directory that does not answer is a bad gateway.
    private static ProblemHttpResult Unavailable(LdapUnavailableException exception) =>
        exception.Reason is { } reason
            ? ServerProblems.Problem(reason, StatusCodes.Status502BadGateway)
            : TypedResults.Problem(title: exception.Message, statusCode: StatusCodes.Status502BadGateway);

    private static ProblemHttpResult Conflict(ServerMessage message) => ServerProblems.Problem(message, StatusCodes.Status409Conflict);
}

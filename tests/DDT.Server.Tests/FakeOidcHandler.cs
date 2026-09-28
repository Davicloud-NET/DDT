// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DDT.Server.Tests;

// Stands in for the OpenID Connect handler and its provider: a challenge signs the subject the query names into the
// external cookie and redirects where the challenge says, as the real callback does. The query's groups become groups
// claims, one each or one JSON array.
public sealed class FakeOidcHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SubjectParameter = "subject";

    public const string GroupsParameter = "groups";

    public const string GroupsArrayParameter = "groupsArray";

    public static string UserNameOf(string subject) => "sso-" + subject;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        string subject = Request.Query[SubjectParameter].ToString();
        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.NameIdentifier, subject), new Claim(ClaimTypes.Name, UserNameOf(subject))],
            Scheme.Name);

        foreach (string group in Request.Query[GroupsParameter].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            identity.AddClaim(new Claim("groups", group));
        }

        if (Request.Query[GroupsArrayParameter].ToString() is { Length: > 0 } array)
        {
            identity.AddClaim(new Claim("groups", JsonSerializer.Serialize(array.Split(','))));
        }

        ClaimsPrincipal principal = new(identity);

        await Context.SignInAsync(IdentityConstants.ExternalScheme, principal, properties);
        Response.Redirect(properties.RedirectUri!);
    }
}

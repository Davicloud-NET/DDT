// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Machines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Authentication;

public static class DdtAuthorizationExtensions
{
    public static IServiceCollection AddDdtAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes(DdtAuthenticationSchemes.User)
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(DdtPolicies.Administrator, policy => policy
                .AddAuthenticationSchemes(DdtAuthenticationSchemes.User)
                .RequireAuthenticatedUser()
                .RequireRole(DdtRoleNames.Administrator)
                .RequireAssertion(HasOwnPassword))
            .AddPolicy(DdtPolicies.Operator, policy => policy
                .AddAuthenticationSchemes(DdtAuthenticationSchemes.User)
                .RequireAuthenticatedUser()
                .RequireRole(DdtRoleNames.Administrator, DdtRoleNames.Operator)
                .RequireAssertion(HasOwnPassword))
            .AddPolicy(DdtPolicies.Viewer, policy => policy
                .AddAuthenticationSchemes(DdtAuthenticationSchemes.User)
                .RequireAuthenticatedUser()
                .RequireRole(DdtRoleNames.Administrator, DdtRoleNames.Operator, DdtRoleNames.Viewer)
                .RequireAssertion(HasOwnPassword))
            .AddPolicy(DdtPolicies.Machine, policy => policy
                .AddAuthenticationSchemes(DdtAuthenticationSchemes.Machine)
                .RequireAuthenticatedUser()
                .RequireClaim(DdtClaimTypes.TokenPurpose, nameof(MachineTokenPurpose.Session)))
            .AddPolicy(DdtPolicies.MachineAgent, policy => policy
                .AddAuthenticationSchemes(DdtAuthenticationSchemes.Machine)
                .RequireAuthenticatedUser());

        return services;
    }

    // An account that signed in with a password an administrator has seen can only reach the Account page's own
    // endpoints, which the fallback policy guards. That lasts until the user sets a password nobody else knows.
    private static bool HasOwnPassword(AuthorizationHandlerContext context) =>
        !context.User.HasClaim(claim => claim.Type == DdtClaimTypes.MustChangePassword);
}

using DDT.Server.Machines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Authentication;

public static class DdtAuthorizationExtensions
{
    public static IServiceCollection AddDdtAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(DdtPolicies.Administrator, policy => policy
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
                .RequireAuthenticatedUser()
                .RequireRole(DdtRoleNames.Administrator))
            .AddPolicy(DdtPolicies.Operator, policy => policy
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
                .RequireAuthenticatedUser()
                .RequireRole(DdtRoleNames.Administrator, DdtRoleNames.Operator))
            .AddPolicy(DdtPolicies.Viewer, policy => policy
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
                .RequireAuthenticatedUser()
                .RequireRole(DdtRoleNames.Administrator, DdtRoleNames.Operator, DdtRoleNames.Viewer))
            .AddPolicy(DdtPolicies.Machine, policy => policy
                .AddAuthenticationSchemes(DdtAuthenticationSchemes.Machine)
                .RequireAuthenticatedUser()
                .RequireClaim(DdtClaimTypes.TokenPurpose, nameof(MachineTokenPurpose.Session)))
            .AddPolicy(DdtPolicies.MachineAgent, policy => policy
                .AddAuthenticationSchemes(DdtAuthenticationSchemes.Machine)
                .RequireAuthenticatedUser());

        return services;
    }
}

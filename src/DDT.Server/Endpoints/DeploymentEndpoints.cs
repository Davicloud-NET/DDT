using DDT.Contracts.Deployments;
using DDT.Server.Authentication;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace DDT.Server.Endpoints;

public static class DeploymentEndpoints
{
    public static RouteGroupBuilder MapDeploymentEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // The assign dialog asks for a computer name only when the machine joins a domain, and says what the
        // assignment does to a waiting machine. The settings themselves stay on the server: they hold the
        // deployment passwords.
        group.MapGet("/options", ReadOptions).RequireAuthorization(DdtPolicies.Viewer);

        return group;
    }

    private static Ok<DeploymentOptionsView> ReadOptions(DeploymentService deployments, IOptions<MachineOptions> machineOptions) =>
        TypedResults.Ok(new DeploymentOptionsView(
            deployments.DomainConfigured,
            machineOptions.Value.RequireWebApproval,
            deployments.ZeroTouchEnabled));
}

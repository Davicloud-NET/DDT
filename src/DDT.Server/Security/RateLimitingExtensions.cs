using System.Threading.RateLimiting;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Security;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddDdtRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.AddPolicy<string>(RateLimitPolicies.SignIn, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(5),
                    QueueLimit = 0,
                }));

            // A site behind one address can boot a whole lab at once, so this is generous. Registration is open
            // to anyone who reaches the server; this and the cap on waiting machines keep that from flooding it.
            limiter.AddPolicy<string>(RateLimitPolicies.AgentRegistration, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            // Partitioned by the machine in the route, so one machine's token cannot starve another machine.
            limiter.AddPolicy<string>(RateLimitPolicies.AgentMachine, context => RateLimitPartition.GetFixedWindowLimiter(
                context.GetRouteValue("id")?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = MachineLogLimits.MaxAgentRequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsync("Too many requests.", cancellationToken)
                    .ConfigureAwait(false);
            };
        });

        return services;
    }
}

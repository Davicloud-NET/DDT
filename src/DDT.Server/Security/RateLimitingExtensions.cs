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

            // A site behind one address can boot a whole lab at once, so these are generous. Registration is open
            // to anyone who reaches the server; this and the cap on waiting machines keep that from flooding it.
            limiter.AddPolicy<string>(RateLimitPolicies.AgentRegistration, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            limiter.AddPolicy<string>(RateLimitPolicies.AgentRelease, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            // Every machine of a lab downloads the agent as it boots. They are served a few at a time per address
            // and the rest wait their turn, rather than being refused and staying on an older agent.
            limiter.AddPolicy<string>(RateLimitPolicies.AgentDownload, context => RateLimitPartition.GetConcurrencyLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = 4,
                    QueueLimit = 64,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
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

            // Image downloads get their own window, so resuming one never eats into the reports of the same run. A
            // machine asks once before it erases the disk and again for each resumed range.
            limiter.AddPolicy<string>(RateLimitPolicies.AgentImage, context => RateLimitPartition.GetFixedWindowLimiter(
                context.GetRouteValue("id")?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
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

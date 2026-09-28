// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DDT.Server.Live;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Rules;

// Every rule says how many known machines it matches, so the list goes out again when the machines change: when one
// registers with something a rule may test, or machines are removed. A lab of machines netbooting at once registers
// many, so it goes out at most once per interval, counted after the last of them, in a scope of its own.
public sealed partial class RuleRecount(
    IServiceScopeFactory scopes,
    LiveNotifier live,
    TimeProvider timeProvider,
    IHostApplicationLifetime lifetime,
    ILogger<RuleRecount> logger)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly PushThrottle _throttle = new(timeProvider, Interval, lifetime.ApplicationStopping);

    public void MachinesChanged() => _throttle.Push(Guid.Empty, PushAsync);

    private async Task PushAsync()
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();

            if (await database.Rules.AnyAsync(lifetime.ApplicationStopping).ConfigureAwait(false))
            {
                live.RulesChanged(await RuleViews.ListAsync(database, lifetime.ApplicationStopping).ConfigureAwait(false));
            }
        }
        // Stopping cancels the count, or disposes of what it needs.
        catch (Exception) when (lifetime.ApplicationStopping.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogRecountFailed(exception);
        }
    }

    [LoggerMessage(EventId = 432, Level = LogLevel.Warning, Message = "Could not count the machines the rules match")]
    private partial void LogRecountFailed(Exception exception);
}

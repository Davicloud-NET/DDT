// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Rules;

// Every rule shows how many machines it matches. So the list is pushed again when a registration changes what a rule
// may test, or machines are removed. It's pushed at most once per interval, in its own scope. That way a whole lab
// netbooting at once causes one push, after the last machine.
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

    // The machine fields a registration sets that a rule's condition may test. Tells whether the counts changed.
    public static string Tested(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return string.Join(
            '|',
            machine.PrimaryMac,
            machine.MacAddresses,
            machine.Manufacturer,
            machine.Model,
            machine.SerialNumber,
            machine.AgentEnvironment,
            machine.SecureBootEnabled,
            machine.ChassisType,
            machine.Facts);
    }

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
        // Stopping the host cancels the count or disposes of what it needs.
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

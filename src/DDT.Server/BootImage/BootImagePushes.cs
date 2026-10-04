// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.BootImage;
using DDT.Server.Data;
using DDT.Server.Live;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.BootImage;

// Sends open pages what a job changed: the view when its state or the build changes, and its output as it comes.
public sealed class BootImagePushes(BootImageViews views, LiveNotifier live, IServiceScopeFactory scopes)
{
    public async Task ViewChangedAsync(CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = scopes.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
            live.BootImageChanged(await views.ViewAsync(database, cancellationToken).ConfigureAwait(false));
        }
    }

    public void Output(BootImageJobOutput output) => live.BootImageJobOutput(output);
}

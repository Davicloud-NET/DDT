// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Import;
using DDT.Contracts.Messages;
using DDT.Contracts.Packages;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Packages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Import;

// Brings files from the server's own disks into the library, and an MDT share's applications after them, one import at
// a time.
public sealed partial class LibraryImports(
    CurrentImport state,
    LibraryFileImport files,
    MdtApplicationImporter applications,
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    ILogger<LibraryImports> logger)
{
    // The import that was started last, for tests to wait on.
    public Task Last { get; private set; } = Task.CompletedTask;

    // Returns false while another import runs.
    public bool TryStart(IReadOnlyList<ImportItem> items, Actor actor, MdtApplicationChoice? choice = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(actor);

        if (!state.TryBegin(actor.Name ?? "unknown", items.Count + (choice?.Chosen.Count ?? 0)))
        {
            return false;
        }

        Last = Task.Run(() => RunAsync(items, choice, actor));

        return true;
    }

    private async Task RunAsync(IReadOnlyList<ImportItem> items, MdtApplicationChoice? choice, Actor actor)
    {
        try
        {
            files.DeleteStale();

            foreach (ImportItem item in items)
            {
                state.Item(item.Name);
                state.Result(await ImportAsync(item, actor, lifetime.ApplicationStopping).ConfigureAwait(false));
            }

            if (choice is { Chosen.Count: > 0 })
            {
                await applications.ImportAsync(choice, actor, lifetime.ApplicationStopping).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            // Told on the page by what is missing from the results. An import must never take the host down.
            LogFailed(exception);
        }

        state.Finish();
    }

    private async Task<ImportResult> ImportAsync(ImportItem item, Actor actor, CancellationToken cancellationToken)
    {
        UploadCompletion completion = await files.AddAsync(item, actor, cancellationToken).ConfigureAwait(false);

        if (completion.Status == UploadCompletionStatus.Refused)
        {
            ServerMessage reason = completion.Refusal ?? ServerMessages.ImportFailed.With();

            return new ImportResult(item.Name, ImportOutcome.Refused, reason, reason.Text);
        }

        await DescribeAsync(item, completion, actor, cancellationToken).ConfigureAwait(false);

        return new ImportResult(item.Name, completion.Status == UploadCompletionStatus.Added ? ImportOutcome.Added : ImportOutcome.Existing);
    }

    // A driver group's package takes the group's name and the model its path named.
    private async Task DescribeAsync(ImportItem item, UploadCompletion completion, Actor actor, CancellationToken cancellationToken)
    {
        if (completion is not { Status: UploadCompletionStatus.Added, Package: { } package })
        {
            return;
        }

        AsyncServiceScope scope = scopes.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            UpdatePackageRequest request = new(
                LibraryEntries.Bounded(item.Name, PackageLimits.MaxNameLength),
                item.Description,
                item.Target is null ? [] : [item.Target]);
            await scope.ServiceProvider.GetRequiredService<PackageLibrary>().UpdateAsync(package.Id, request, actor, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 920, Level = LogLevel.Warning, Message = "An import into the library failed")]
    private partial void LogFailed(Exception exception);
}

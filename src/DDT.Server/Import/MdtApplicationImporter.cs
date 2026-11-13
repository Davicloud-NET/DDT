// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Images;
using DDT.Contracts.Import;
using DDT.Contracts.Messages;
using DDT.Core.Applications;
using DDT.Server.Applications;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Images;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Import;

// Imports an MDT share's chosen applications, each after its dependencies, then its bundles. Files go the way of an
// upload and entries through the editors, so the library's checks, audit rows and live events apply as on the page. A
// name the library has already is left as it is.
public sealed class MdtApplicationImporter(CurrentImport state, LibraryFileImport files, IServiceScopeFactory scopes)
{
    public async Task ImportAsync(MdtApplicationChoice choice, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(actor);

        Dictionary<string, MdtShareApplication> entries = choice.Entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        List<MdtShareApplication> applications = [.. choice.Chosen.Where(entry => !entry.IsBundle)];
        Dictionary<string, IReadOnlyList<string>> dependencies = applications.ToDictionary(
            application => application.Id,
            application => MdtApplicationGraph.Flatten(application, entries),
            StringComparer.Ordinal);
        MdtApplicationOrder order = MdtApplicationGraph.Order(applications, dependencies);
        Run run = new(choice.Share, await LinksAsync(entries, order.Cut, cancellationToken).ConfigureAwait(false), actor);

        foreach (MdtShareApplication application in order.Applications)
        {
            state.Item(application.Name);
            state.Result(await ApplicationAsync(run, application, dependencies[application.Id], cancellationToken).ConfigureAwait(false));
        }

        foreach (MdtShareApplication bundle in choice.Chosen.Where(entry => entry.IsBundle))
        {
            state.Item(bundle.Name);
            state.Result(await BundleAsync(run, bundle, MdtApplicationGraph.Flatten(bundle, entries), cancellationToken).ConfigureAwait(false));
        }
    }

    private async Task<ImportResult> ApplicationAsync(Run run, MdtShareApplication application, IReadOnlyList<string> dependencies, CancellationToken cancellationToken)
    {
        MdtApplicationLinks links = run.Links;

        if (links.Named(application.Name) is { } existing)
        {
            if (existing.IsApplication)
            {
                links.Link(application, existing.Id);
            }

            return new ImportResult(application.Name, ImportOutcome.Existing);
        }

        List<ServerMessage> notes = [];
        IReadOnlyList<Guid> dependsOn = links.Resolve(application, dependencies, notes);

        // The rules are checked before the files are read, which can take a while.
        if (ApplicationRules.Check(MdtApplicationRequests.Of(application, null, dependsOn), null) is [{ Message: var problem }, ..])
        {
            return Refused(application, problem);
        }

        (ApplicationUpload? upload, ServerMessage? refusal) = await FilesAsync(run, application, cancellationToken).ConfigureAwait(false);

        if (refusal is not null)
        {
            return Refused(application, refusal);
        }

        if (MdtApplicationRequests.WorkingDirectoryNote(application) is { } note)
        {
            notes.Add(note);
        }

        if (MdtApplicationRequests.MdtNameNote(application) is { } mdtName)
        {
            notes.Add(mdtName);
        }

        AsyncServiceScope scope = scopes.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            EditOutcome<ApplicationView> outcome = await scope.ServiceProvider.GetRequiredService<ApplicationEditor>()
                .CreateAsync(MdtApplicationRequests.Of(application, upload?.Files.PackageId, dependsOn, upload?.Suggestion), run.Actor, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.Saved is not { } saved)
            {
                return Refused(application, Problem(outcome));
            }

            links.AddLibrary(saved.Name, saved.Id, isApplication: true);
            links.Link(application, saved.Id);

            return new ImportResult(application.Name, ImportOutcome.Added, Notes: notes);
        }
    }

    // The upload of the Source folder's files, or none for an application without.
    private async Task<(ApplicationUpload? Upload, ServerMessage? Refusal)> FilesAsync(Run run, MdtShareApplication application, CancellationToken cancellationToken)
    {
        if (!application.HasFiles)
        {
            return (null, null);
        }

        if (MdtShare.Resolve(run.Share, application.Source) is not { } folder || !Directory.Exists(folder))
        {
            return (null, ServerMessages.ImportNoSuchFile.With("path", application.Source ?? ""));
        }

        UploadCompletion completion = await files.AddAsync(new ImportItem(UploadKind.Application, application.Name, null, [folder]), run.Actor, cancellationToken)
            .ConfigureAwait(false);

        return completion.Application is { } upload
            ? (upload, null)
            : (null, completion.Refusal ?? ServerMessages.ImportFailed.With());
    }

    // A bundle's members are its dependencies' applications, with those of bundles in it.
    private async Task<ImportResult> BundleAsync(Run run, MdtShareApplication bundle, IReadOnlyList<string> members, CancellationToken cancellationToken)
    {
        MdtApplicationLinks links = run.Links;

        if (links.Named(bundle.Name) is not null)
        {
            return new ImportResult(bundle.Name, ImportOutcome.Existing);
        }

        List<ServerMessage> notes = [];
        IReadOnlyList<Guid> resolved = links.Resolve(bundle, members, notes);

        if (resolved.Count == 0)
        {
            return Refused(bundle, ServerMessages.ImportBundleEmpty.With());
        }

        AsyncServiceScope scope = scopes.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            SaveApplicationBundleRequest request = new(0, bundle.Name, LibraryEntries.Bounded(bundle.Comments, ApplicationLimits.MaxDescriptionLength), resolved);
            EditOutcome<ApplicationBundleView> outcome = await scope.ServiceProvider.GetRequiredService<ApplicationBundleEditor>()
                .CreateAsync(request, run.Actor, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.Saved is not { } saved)
            {
                return Refused(bundle, Problem(outcome));
            }

            links.AddLibrary(saved.Name, saved.Id, isApplication: false);

            return new ImportResult(bundle.Name, ImportOutcome.Added, Notes: notes);
        }
    }

    private async Task<MdtApplicationLinks> LinksAsync(
        IReadOnlyDictionary<string, MdtShareApplication> entries,
        IReadOnlySet<(string Application, string Dependency)> cut,
        CancellationToken cancellationToken)
    {
        MdtApplicationLinks links = new(entries, cut);
        AsyncServiceScope scope = scopes.CreateAsyncScope();

        await using (scope.ConfigureAwait(false))
        {
            ApplicationRows rows = await scope.ServiceProvider.GetRequiredService<ApplicationViews>().RowsAsync(cancellationToken).ConfigureAwait(false);

            foreach (Application application in rows.Applications.Values)
            {
                links.AddLibrary(application.Name, application.Id, isApplication: true);
            }

            foreach (ApplicationBundle bundle in rows.Bundles)
            {
                links.AddLibrary(bundle.Name, bundle.Id, isApplication: false);
            }
        }

        return links;
    }

    private static ServerMessage Problem<T>(EditOutcome<T> outcome)
        where T : class =>
        outcome.Refusal ?? outcome.Problems?.Messages.FirstOrDefault() ?? ServerMessages.ImportFailed.With();

    private static ImportResult Refused(MdtShareApplication entry, ServerMessage reason) => new(entry.Name, ImportOutcome.Refused, reason, reason.Text);

    private sealed record Run(string Share, MdtApplicationLinks Links, Actor Actor);
}

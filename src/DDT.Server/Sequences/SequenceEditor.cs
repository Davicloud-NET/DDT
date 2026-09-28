// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Server.Accounts;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Rules;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Sequences;

// Saves task sequences, with an audit row for each change. A save is only refused if the document can't be stored.
// Every other problem is saved with it, so an editor that saves while the administrator types keeps a draft.
internal sealed class SequenceEditor(
    DdtDbContext database,
    SequenceCatalog catalog,
    AccountViews accounts,
    LiveNotifier live,
    TimeProvider timeProvider)
{
    public async Task<EditOutcome<SequenceView>> CreateAsync(CreateSequenceRequest request, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        if (await FieldProblemsAsync(null, request.Name, request.Description, cancellationToken).ConfigureAwait(false) is { } problems)
        {
            return EditOutcome<SequenceView>.Invalid(problems);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string name = request.Name.Trim();
        TaskSequence sequence = new()
        {
            Id = Guid.CreateVersion7(now),
            Name = name,
            NormalizedName = Normalize(name),
            Description = Description(request.Description),
            Definition = SequenceDocuments.Write(request.Definition),
            Revision = 1,
            CreatedUtc = now,
            UpdatedUtc = now,
            UpdatedByUserId = actor.UserId,
            UpdatedByName = actor.Name,
        };

        database.TaskSequences.Add(sequence);
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.SequenceCreated,
            sequence.Id.ToString("D"),
            actor,
            now,
            $"{name}, {request.Definition.Steps.Count} steps."));

        if (await CommitAsync(sequence, cancellationToken).ConfigureAwait(false) is { } taken)
        {
            return EditOutcome<SequenceView>.Invalid(taken);
        }

        live.SequenceChanged(new SequenceChangedEvent(sequence.Id, sequence.Revision, sequence.UpdatedByName));
        await accounts.PushUsesAsync(live, AccountViews.UsesChanged(null, null, sequence.Definition, sequence.Name), cancellationToken).ConfigureAwait(false);

        return EditOutcome<SequenceView>.Done(await catalog.ViewAsync(sequence, cancellationToken).ConfigureAwait(false));
    }

    // A save names the revision it was based on. If the sequence has a newer revision, the save gets the current one.
    public async Task<EditOutcome<SequenceView>> SaveAsync(Guid id, SaveSequenceRequest request, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);

        TaskSequence? sequence = await database.TaskSequences.FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);

        if (sequence is null)
        {
            return EditOutcome<SequenceView>.NotFound;
        }

        if (request.Revision != sequence.Revision)
        {
            return EditOutcome<SequenceView>.Newer(await catalog.ViewAsync(sequence, cancellationToken).ConfigureAwait(false));
        }

        if (await FieldProblemsAsync(id, request.Name, request.Description, cancellationToken).ConfigureAwait(false) is { } problems)
        {
            return EditOutcome<SequenceView>.Invalid(problems);
        }

        string name = request.Name.Trim();
        string? description = Description(request.Description);
        string definition = SequenceDocuments.Write(request.Definition);

        // An autosave of what's already stored changes nothing, so nothing is recorded.
        if (name == sequence.Name && description == sequence.Description && definition == sequence.Definition)
        {
            return EditOutcome<SequenceView>.Done(await catalog.ViewAsync(sequence, cancellationToken).ConfigureAwait(false));
        }

        (string savedName, string savedDefinition) = (sequence.Name, sequence.Definition);
        string changes = SequenceChanges.Describe(
            new SequenceContent(sequence.Name, sequence.Description, SequenceDocuments.Read(sequence.Definition)),
            new SequenceContent(name, description, request.Definition));

        sequence.Name = name;
        sequence.NormalizedName = Normalize(name);
        sequence.Description = description;
        sequence.Definition = definition;
        sequence.Revision++;
        sequence.UpdatedUtc = timeProvider.GetUtcNow();
        sequence.UpdatedByUserId = actor.UserId;
        sequence.UpdatedByName = actor.Name;
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.SequenceChanged,
            sequence.Id.ToString("D"),
            actor,
            sequence.UpdatedUtc,
            $"Revision {sequence.Revision}. {changes}"));

        return await SaveChangedAsync(sequence, savedName, savedDefinition, cancellationToken).ConfigureAwait(false);
    }

    // Found is false if the sequence doesn't exist. Refusal says why it can't be deleted.
    public async Task<(bool Found, ServerMessage? Refusal)> DeleteAsync(Guid id, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        TaskSequence? sequence = await database.TaskSequences.FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);

        if (sequence is null)
        {
            return (false, null);
        }

        int rules = await database.Rules.CountAsync(r => r.TaskSequenceId == id, cancellationToken).ConfigureAwait(false);

        if (rules > 0)
        {
            return (true, ServerMessages.SequenceChosenByRules.With("count", rules));
        }

        database.TaskSequences.Remove(sequence);
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.SequenceDeleted,
            sequence.Id.ToString("D"),
            actor,
            timeProvider.GetUtcNow(),
            $"{sequence.Name}, revision {sequence.Revision}."));

        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        // Either a save raised the revision in the meantime, or a new rule now holds the sequence by its foreign key.
        catch (DbUpdateException)
        {
            return (true, ServerMessages.SequenceChangedWhileDeleting.With());
        }

        live.SequenceChanged(new SequenceChangedEvent(sequence.Id, null, actor.Name));
        await accounts.PushUsesAsync(live, AccountViews.UsesChanged(sequence.Definition, sequence.Name, null, null), cancellationToken).ConfigureAwait(false);

        return (true, null);
    }

    // savedName and savedDefinition are the values before this save. They tell which account uses to push again.
    private async Task<EditOutcome<SequenceView>> SaveChangedAsync(
        TaskSequence sequence,
        string savedName,
        string savedDefinition,
        CancellationToken cancellationToken)
    {
        try
        {
            if (await CommitAsync(sequence, cancellationToken).ConfigureAwait(false) is { } taken)
            {
                return EditOutcome<SequenceView>.Invalid(taken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            TaskSequence? current = await database.TaskSequences
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sequence.Id, cancellationToken)
                .ConfigureAwait(false);

            return current is null
                ? EditOutcome<SequenceView>.NotFound
                : EditOutcome<SequenceView>.Newer(await catalog.ViewAsync(current, cancellationToken).ConfigureAwait(false));
        }

        live.SequenceChanged(new SequenceChangedEvent(sequence.Id, sequence.Revision, sequence.UpdatedByName));
        await accounts
            .PushUsesAsync(live, AccountViews.UsesChanged(savedDefinition, savedName, sequence.Definition, sequence.Name), cancellationToken)
            .ConfigureAwait(false);

        // A rule shows the name of the sequence it chooses.
        if (savedName != sequence.Name && await database.Rules.AnyAsync(r => r.TaskSequenceId == sequence.Id, cancellationToken).ConfigureAwait(false))
        {
            live.RulesChanged(await RuleViews.ListAsync(database, cancellationToken).ConfigureAwait(false));
        }

        return EditOutcome<SequenceView>.Done(await catalog.ViewAsync(sequence, cancellationToken).ConfigureAwait(false));
    }

    private async Task<FieldProblems?> FieldProblemsAsync(Guid? id, string? name, string? description, CancellationToken cancellationToken)
    {
        FieldProblems problems = new();
        string trimmed = name?.Trim() ?? "";

        if (trimmed.Length is 0 or > SequenceLimits.MaxNameLength || trimmed.Any(char.IsControl))
        {
            problems.Add("name", ServerMessages.NameLength.With("max", SequenceLimits.MaxNameLength));
        }
        else if (await NameTakenAsync(id, trimmed, cancellationToken).ConfigureAwait(false))
        {
            problems.Add("name", ServerMessages.SequenceNameTaken.With("name", trimmed));
        }

        if (Description(description)?.Length > SequenceLimits.MaxDescriptionLength)
        {
            problems.Add("description", ServerMessages.DescriptionLength.With("max", SequenceLimits.MaxDescriptionLength));
        }

        return problems.Count > 0 ? problems : null;
    }

    private Task<bool> NameTakenAsync(Guid? id, string name, CancellationToken cancellationToken)
    {
        string normalized = Normalize(name);

        return database.TaskSequences.AnyAsync(s => s.NormalizedName == normalized && s.Id != id, cancellationToken);
    }

    // The unique index decides between two saves that take the same name at once. The loser is told the name is taken,
    // just as if the other save had come first.
    private async Task<FieldProblems?> CommitAsync(TaskSequence sequence, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return null;
        }
        catch (DbUpdateException exception) when (exception is not DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();

            if (!await NameTakenAsync(sequence.Id, sequence.Name, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }

            FieldProblems taken = new();
            taken.Add("name", ServerMessages.SequenceNameTaken.With("name", sequence.Name));

            return taken;
        }
    }

    private static string Normalize(string name) => name.Trim().ToUpperInvariant();

    // PostgreSQL text cannot hold a NUL.
    private static string? Description(string? description)
    {
        string? text = description?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();

        return string.IsNullOrEmpty(text) ? null : text;
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Settings;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DDT.Server.Settings;

// The agent and the console that netbooting machines get, as the settings page shows and replaces them. Who uploaded
// one and when come from the audit row of its latest upload.
internal sealed class ReleaseUploads(
    AgentReleaseStore releases,
    ConsoleReleaseStore consoles,
    IOptions<AgentReleaseOptions> options,
    DdtDbContext database,
    TimeProvider timeProvider,
    LiveNotifier live)
{
    // Configuration names a file of its own, which an upload would not replace.
    public bool AgentConfigured => !string.IsNullOrWhiteSpace(options.Value.BinaryPath);

    public bool ConsoleConfigured => !string.IsNullOrWhiteSpace(options.Value.ConsolePath);

    public async Task<AgentBinaryView> AgentAsync(CancellationToken cancellationToken)
    {
        AgentRelease? release = await releases.CurrentAsync(cancellationToken).ConfigureAwait(false);
        AuditEvent? upload = AgentConfigured || release is null
            ? null
            : await LatestAsync(AuditActions.AgentUploaded, cancellationToken).ConfigureAwait(false);

        return new AgentBinaryView(
            release?.Sha256,
            release?.Size,
            upload?.OccurredUtc,
            upload?.ActorName,
            AgentConfigured ? AgentBinarySource.Configuration : release is null ? AgentBinarySource.None : AgentBinarySource.Uploaded);
    }

    public async Task<AgentBinaryView> ConsoleAsync(CancellationToken cancellationToken)
    {
        ConsoleRelease? release = await consoles.CurrentAsync(cancellationToken).ConfigureAwait(false);
        AuditEvent? upload = ConsoleConfigured || release is null
            ? null
            : await LatestAsync(AuditActions.ConsoleUploaded, cancellationToken).ConfigureAwait(false);

        return new AgentBinaryView(
            release?.Files[0].Sha256,
            release?.Files.Sum(file => file.Size),
            upload?.OccurredUtc,
            upload?.ActorName,
            ConsoleConfigured ? AgentBinarySource.Configuration : release is null ? AgentBinarySource.None : AgentBinarySource.Uploaded);
    }

    // The view is the one GET /api/settings/agent reads from now on, which other administrators' pages take from the hub.
    public async Task<(ReleaseUploadStatus Status, AgentBinaryView? View)> UploadAgentAsync(Stream content, Actor actor, CancellationToken cancellationToken)
    {
        (ReleaseUploadStatus status, AgentRelease? release) = await releases.SaveAsync(content, cancellationToken).ConfigureAwait(false);

        if (release is null)
        {
            return (status, null);
        }

        AuditEvent audit = await AuditAsync(
            AuditActions.AgentUploaded,
            release.Sha256,
            actor,
            $"Uploaded the agent with SHA-256 {release.Sha256}, {release.Size} bytes. Netbooting machines run it from their next boot.",
            cancellationToken).ConfigureAwait(false);

        AgentBinaryView uploaded = new(release.Sha256, release.Size, audit.OccurredUtc, audit.ActorName, AgentBinarySource.Uploaded);
        live.AgentChanged(uploaded);

        return (status, uploaded);
    }

    public async Task<(ReleaseUploadStatus Status, AgentBinaryView? View)> UploadConsoleAsync(Stream content, Actor actor, CancellationToken cancellationToken)
    {
        (ReleaseUploadStatus status, ConsoleRelease? release) = await consoles.SaveAsync(content, cancellationToken).ConfigureAwait(false);

        if (release is null)
        {
            return (status, null);
        }

        string sha256 = release.Files[0].Sha256;
        long size = release.Files.Sum(file => file.Size);
        AuditEvent audit = await AuditAsync(
            AuditActions.ConsoleUploaded,
            sha256,
            actor,
            $"Uploaded the console with ddt-console.exe of SHA-256 {sha256}, {size} bytes in all. Netbooting machines show it from their next boot.",
            cancellationToken).ConfigureAwait(false);

        AgentBinaryView uploaded = new(sha256, size, audit.OccurredUtc, audit.ActorName, AgentBinarySource.Uploaded);
        live.ConsoleChanged(uploaded);

        return (status, uploaded);
    }

    private Task<AuditEvent?> LatestAsync(string action, CancellationToken cancellationToken) =>
        database.AuditEvents
            .AsNoTracking()
            .Where(audit => audit.Action == action)
            .OrderByDescending(audit => audit.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<AuditEvent> AuditAsync(string action, string sha256, Actor actor, string detail, CancellationToken cancellationToken)
    {
        AuditEvent audit = AuditEvents.Create(action, sha256, actor, timeProvider.GetUtcNow(), detail);

        database.AuditEvents.Add(audit);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return audit;
    }
}

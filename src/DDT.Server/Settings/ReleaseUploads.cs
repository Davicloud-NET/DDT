// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Settings;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Settings;

// The agent and console builds that netbooting machines get. The settings page shows them, replaces them and removes
// an upload again. Who uploaded one and when comes from the audit row of its latest upload.
internal sealed class ReleaseUploads(
    AgentReleaseStore releases,
    ConsoleReleaseStore consoles,
    DdtDbContext database,
    TimeProvider timeProvider,
    LiveNotifier live)
{
    public bool AgentConfigured => releases.Configured;

    public bool ConsoleConfigured => consoles.Configured;

    public async Task<AgentBinaryView> AgentAsync(CancellationToken cancellationToken)
    {
        StoredAgent? offered = await releases.OfferedAsync(cancellationToken).ConfigureAwait(false);
        bool uploaded = offered?.Source == AgentBinarySource.Uploaded;
        StoredAgent? bundled = uploaded ? await releases.BundledAsync(cancellationToken).ConfigureAwait(false) : null;
        AuditEvent? upload = uploaded ? await LatestAsync(AuditActions.AgentUploaded, cancellationToken).ConfigureAwait(false) : null;

        return new AgentBinaryView(
            offered?.Release.Sha256,
            offered?.Release.Size,
            upload?.OccurredUtc,
            upload?.ActorName,
            offered?.Source ?? (AgentConfigured ? AgentBinarySource.Configuration : AgentBinarySource.None),
            Text(offered?.Version),
            Text(Newer(bundled?.Version, offered?.Version)));
    }

    public async Task<AgentBinaryView> ConsoleAsync(CancellationToken cancellationToken)
    {
        StoredConsole? offered = await consoles.OfferedAsync(cancellationToken).ConfigureAwait(false);
        bool uploaded = offered?.Source == AgentBinarySource.Uploaded;
        StoredConsole? bundled = uploaded ? await consoles.BundledAsync(cancellationToken).ConfigureAwait(false) : null;
        AuditEvent? upload = uploaded ? await LatestAsync(AuditActions.ConsoleUploaded, cancellationToken).ConfigureAwait(false) : null;

        return new AgentBinaryView(
            offered?.Release.Files[0].Sha256,
            offered?.Release.Files.Sum(file => file.Size),
            upload?.OccurredUtc,
            upload?.ActorName,
            offered?.Source ?? (ConsoleConfigured ? AgentBinarySource.Configuration : AgentBinarySource.None),
            Text(offered?.Version),
            Text(Newer(bundled?.Version, offered?.Version)));
    }

    // The returned view is what GET /api/settings/agent reads from now on. Other administrators' pages get it from the
    // hub.
    public async Task<(ReleaseUploadStatus Status, AgentBinaryView? View)> UploadAgentAsync(Stream content, Actor actor, CancellationToken cancellationToken)
    {
        (ReleaseUploadStatus status, AgentRelease? release) = await releases.SaveAsync(content, cancellationToken).ConfigureAwait(false);

        if (release is null)
        {
            return (status, null);
        }

        await AuditAsync(
            AuditActions.AgentUploaded,
            release.Sha256,
            actor,
            $"Uploaded the agent with SHA-256 {release.Sha256}, {release.Size} bytes. Netbooting machines run it from their next boot.",
            cancellationToken).ConfigureAwait(false);

        AgentBinaryView uploaded = await AgentAsync(cancellationToken).ConfigureAwait(false);
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
        await AuditAsync(
            AuditActions.ConsoleUploaded,
            sha256,
            actor,
            $"Uploaded the console with ddt-console.exe of SHA-256 {sha256}, {size} bytes in all. Netbooting machines show it from their next boot.",
            cancellationToken).ConfigureAwait(false);

        AgentBinaryView uploaded = await ConsoleAsync(cancellationToken).ConfigureAwait(false);
        live.ConsoleChanged(uploaded);

        return (status, uploaded);
    }

    // Null when no agent was uploaded. Machines then get the one the server came with, or keep their boot image's.
    public async Task<AgentBinaryView?> RemoveAgentAsync(Actor actor, CancellationToken cancellationToken)
    {
        StoredAgent? offered = await releases.OfferedAsync(cancellationToken).ConfigureAwait(false);

        if (offered?.Source != AgentBinarySource.Uploaded || !releases.RemoveUpload())
        {
            return null;
        }

        await AuditAsync(
            AuditActions.AgentUploadRemoved,
            offered.Release.Sha256,
            actor,
            $"Removed the uploaded agent with SHA-256 {offered.Release.Sha256}. From their next boot, netbooting machines run the agent the server came with, or their boot image's.",
            cancellationToken).ConfigureAwait(false);

        AgentBinaryView now = await AgentAsync(cancellationToken).ConfigureAwait(false);
        live.AgentChanged(now);

        return now;
    }

    public async Task<AgentBinaryView?> RemoveConsoleAsync(Actor actor, CancellationToken cancellationToken)
    {
        StoredConsole? offered = await consoles.OfferedAsync(cancellationToken).ConfigureAwait(false);

        if (offered?.Source != AgentBinarySource.Uploaded || !consoles.RemoveUpload())
        {
            return null;
        }

        string sha256 = offered.Release.Files[0].Sha256;
        await AuditAsync(
            AuditActions.ConsoleUploadRemoved,
            sha256,
            actor,
            $"Removed the uploaded console with ddt-console.exe of SHA-256 {sha256}. From their next boot, netbooting machines show the console the server came with, or their boot image's.",
            cancellationToken).ConfigureAwait(false);

        AgentBinaryView now = await ConsoleAsync(cancellationToken).ConfigureAwait(false);
        live.ConsoleChanged(now);

        return now;
    }

    // Three parts, as a release is numbered
    private static string? Text(Version? version) => version?.ToString(3);

    // The bundled version if it's the newer one. Null when either file carries no version.
    private static Version? Newer(Version? bundled, Version? offered) =>
        bundled is not null && offered is not null && bundled > offered ? bundled : null;

    private Task<AuditEvent?> LatestAsync(string action, CancellationToken cancellationToken) =>
        database.AuditEvents
            .AsNoTracking()
            .Where(audit => audit.Action == action)
            .OrderByDescending(audit => audit.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task AuditAsync(string action, string sha256, Actor actor, string detail, CancellationToken cancellationToken)
    {
        database.AuditEvents.Add(AuditEvents.Create(action, sha256, actor, timeProvider.GetUtcNow(), detail));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

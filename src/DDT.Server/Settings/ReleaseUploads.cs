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
    AgentReleaseStores agents,
    ConsoleReleaseStores consoles,
    DdtDbContext database,
    TimeProvider timeProvider,
    LiveNotifier live)
{
    public bool ConsoleConfigured(ConsoleUpload console) => StoreOf(console).Configured;

    public bool AgentConfigured(AgentUpload agent) => StoreOf(agent).Configured;

    public async Task<AgentBinaryView> AgentAsync(AgentUpload agent, CancellationToken cancellationToken)
    {
        AgentReleaseStore releases = StoreOf(agent);
        StoredAgent? offered = await releases.OfferedAsync(cancellationToken).ConfigureAwait(false);
        bool uploaded = offered?.Source == AgentBinarySource.Uploaded;
        StoredAgent? bundled = uploaded ? await releases.BundledAsync(cancellationToken).ConfigureAwait(false) : null;
        AuditEvent? upload = uploaded ? await LatestAsync(agent.UploadedAction, cancellationToken).ConfigureAwait(false) : null;

        return new AgentBinaryView(
            offered?.Release.Sha256,
            offered?.Release.Size,
            upload?.OccurredUtc,
            upload?.ActorName,
            offered?.Source ?? (releases.Configured ? AgentBinarySource.Configuration : AgentBinarySource.None),
            Text(offered?.Version),
            Text(Newer(bundled?.Version, offered?.Version)));
    }

    public async Task<AgentBinaryView> ConsoleAsync(ConsoleUpload console, CancellationToken cancellationToken)
    {
        ConsoleReleaseStore releases = StoreOf(console);
        StoredConsole? offered = await releases.OfferedAsync(cancellationToken).ConfigureAwait(false);
        bool uploaded = offered?.Source == AgentBinarySource.Uploaded;
        StoredConsole? bundled = uploaded ? await releases.BundledAsync(cancellationToken).ConfigureAwait(false) : null;
        AuditEvent? upload = uploaded ? await LatestAsync(console.UploadedAction, cancellationToken).ConfigureAwait(false) : null;

        return new AgentBinaryView(
            offered?.Release.Files[0].Sha256,
            offered?.Release.Files.Sum(file => file.Size),
            upload?.OccurredUtc,
            upload?.ActorName,
            offered?.Source ?? (releases.Configured ? AgentBinarySource.Configuration : AgentBinarySource.None),
            Text(offered?.Version),
            Text(Newer(bundled?.Version, offered?.Version)));
    }

    // The returned view is what a read of the agent's settings returns from now on. Other administrators' pages get it
    // from the hub.
    public async Task<(ReleaseUploadStatus Status, AgentBinaryView? View)> UploadAgentAsync(
        AgentUpload agent,
        Stream content,
        Actor actor,
        CancellationToken cancellationToken)
    {
        (ReleaseUploadStatus status, AgentRelease? release) = await StoreOf(agent).SaveAsync(content, cancellationToken).ConfigureAwait(false);

        if (release is null)
        {
            return (status, null);
        }

        await AuditAsync(agent.UploadedAction, release.Sha256, actor, agent.UploadedDetail(release), cancellationToken).ConfigureAwait(false);

        AgentBinaryView uploaded = await AgentAsync(agent, cancellationToken).ConfigureAwait(false);
        Push(agent, uploaded);

        return (status, uploaded);
    }

    public async Task<(ReleaseUploadStatus Status, AgentBinaryView? View)> UploadConsoleAsync(
        ConsoleUpload console,
        Stream content,
        Actor actor,
        CancellationToken cancellationToken)
    {
        (ReleaseUploadStatus status, ConsoleRelease? release) = await StoreOf(console).SaveAsync(content, cancellationToken).ConfigureAwait(false);

        if (release is null)
        {
            return (status, null);
        }

        string sha256 = release.Files[0].Sha256;
        long size = release.Files.Sum(file => file.Size);
        await AuditAsync(console.UploadedAction, sha256, actor, console.UploadedDetail(sha256, size), cancellationToken).ConfigureAwait(false);

        AgentBinaryView uploaded = await ConsoleAsync(console, cancellationToken).ConfigureAwait(false);
        Push(console, uploaded);

        return (status, uploaded);
    }

    // Null when no agent was uploaded. Machines then get the one the server came with, or keep the one they started
    // with.
    public async Task<AgentBinaryView?> RemoveAgentAsync(AgentUpload agent, Actor actor, CancellationToken cancellationToken)
    {
        AgentReleaseStore releases = StoreOf(agent);
        StoredAgent? offered = await releases.OfferedAsync(cancellationToken).ConfigureAwait(false);

        if (offered?.Source != AgentBinarySource.Uploaded || !releases.RemoveUpload())
        {
            return null;
        }

        string sha256 = offered.Release.Sha256;
        await AuditAsync(agent.RemovedAction, sha256, actor, agent.RemovedDetail(sha256), cancellationToken).ConfigureAwait(false);

        AgentBinaryView now = await AgentAsync(agent, cancellationToken).ConfigureAwait(false);
        Push(agent, now);

        return now;
    }

    public async Task<AgentBinaryView?> RemoveConsoleAsync(ConsoleUpload console, Actor actor, CancellationToken cancellationToken)
    {
        ConsoleReleaseStore releases = StoreOf(console);
        StoredConsole? offered = await releases.OfferedAsync(cancellationToken).ConfigureAwait(false);

        if (offered?.Source != AgentBinarySource.Uploaded || !releases.RemoveUpload())
        {
            return null;
        }

        string sha256 = offered.Release.Files[0].Sha256;
        await AuditAsync(console.RemovedAction, sha256, actor, console.RemovedDetail(sha256), cancellationToken).ConfigureAwait(false);

        AgentBinaryView now = await ConsoleAsync(console, cancellationToken).ConfigureAwait(false);
        Push(console, now);

        return now;
    }

    // Three parts, as a release is numbered
    private static string? Text(Version? version) => version?.ToString(3);

    // The bundled version if it's the newer one. Null when either file carries no version.
    private static Version? Newer(Version? bundled, Version? offered) =>
        bundled is not null && offered is not null && bundled > offered ? bundled : null;

    private AgentReleaseStore StoreOf(AgentUpload agent) => agent.IsLinux ? agents.Linux : agents.Windows;

    private ConsoleReleaseStore StoreOf(ConsoleUpload console) => console.IsLinux ? consoles.Linux : consoles.Windows;

    private void Push(ConsoleUpload console, AgentBinaryView view)
    {
        if (console.IsLinux)
        {
            live.LinuxConsoleChanged(view);
        }
        else
        {
            live.ConsoleChanged(view);
        }
    }

    private void Push(AgentUpload agent, AgentBinaryView view)
    {
        if (agent.IsLinux)
        {
            live.LinuxAgentChanged(view);
        }
        else
        {
            live.AgentChanged(view);
        }
    }

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

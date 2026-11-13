// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.InstalledPhase;

namespace DDT.Agent.Tests;

// Notes the record among the fake tools' calls, and keeps what it was given. Fails with Failure when set.
internal sealed class FakeDeploymentRecord(FakeDeploymentTools tools) : IDeploymentRecord
{
    public List<DeploymentRecordEntry> Entries { get; } = [];

    public Exception? Failure { get; init; }

    public Task WriteAsync(DeploymentRecordEntry entry, CancellationToken cancellationToken)
    {
        tools.Note($"deployment record {entry.Result}");

        if (Failure is not null)
        {
            throw Failure;
        }

        Entries.Add(entry);

        return Task.CompletedTask;
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.BitLocker;
using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Records each call as "name volume" in Events, which other fakes of a test may share. Without a TPM (HasTpm false)
// the adds answer as manage-bde does. Statuses answer StatusAsync in turn, the last one again and again.
internal sealed class FakeBitLocker(List<string> events) : IBitLocker
{
    // Made up, of the real shape: each group divisible by 11.
    public const string Password = "111111-222222-333333-444444-555555-666666-707707-121121";

    public static readonly Guid ProtectorId = Guid.Parse("748dc9c3-3024-4646-9069-b9efbeed68e4");

    public bool IsInstalled { get; set; } = true;

    public bool HasTpm { get; set; } = true;

    public bool TpmProtector { get; set; }

    public List<Guid> Leftovers { get; } = [];

    public bool BackupFails { get; set; }

    public Queue<VolumeStatus> Statuses { get; } = new();

    private VolumeStatus _last = new(1, 100, true);

    public Task<bool> PreprovisionAsync(string volume, CancellationToken cancellationToken) => Said($"preprovision {volume}", HasTpm);

    public Task<bool> HasTpmProtectorAsync(string volume, CancellationToken cancellationToken) => Said($"has-tpm {volume}", TpmProtector);

    public Task<IReadOnlyList<Guid>> RecoveryPasswordIdsAsync(string volume, CancellationToken cancellationToken) =>
        Said<IReadOnlyList<Guid>>($"list {volume}", [.. Leftovers]);

    public Task DeleteProtectorAsync(string volume, Guid protectorId, CancellationToken cancellationToken)
    {
        Leftovers.Remove(protectorId);

        return Said($"delete {volume} {protectorId:B}", true);
    }

    public Task<bool> AddTpmProtectorAsync(string volume, CancellationToken cancellationToken) => Said($"add-tpm {volume}", HasTpm);

    public Task<RecoveryPassword?> AddRecoveryPasswordAsync(string volume, CancellationToken cancellationToken) =>
        Said($"add-password {volume}", HasTpm ? new RecoveryPassword(ProtectorId, Password) : null);

    public Task BackupToActiveDirectoryAsync(string volume, Guid protectorId, CancellationToken cancellationToken)
    {
        events.Add($"backup {volume} {protectorId:B}");

        return BackupFails
            ? throw new DeploymentStepException($"manage-bde could not back up the recovery password to Active Directory on {volume} (exit code 0x8031006A).")
            : Task.CompletedTask;
    }

    public Task TurnOnAsync(string volume, CancellationToken cancellationToken) => Said($"turn-on {volume}", true);

    public Task<VolumeStatus> StatusAsync(string volume, CancellationToken cancellationToken)
    {
        _last = Statuses.TryDequeue(out VolumeStatus? next) ? next : _last;

        return Said($"status {volume}", _last);
    }

    private Task<T> Said<T>(string call, T result)
    {
        events.Add(call);

        return Task.FromResult(result);
    }
}

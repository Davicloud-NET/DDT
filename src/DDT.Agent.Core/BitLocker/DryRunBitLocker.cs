// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.BitLocker;

// Changes nothing on this computer. It adds a made-up recovery password of the real shape, and takes the volume as
// encrypted and protected at once.
public sealed class DryRunBitLocker(AgentLog log) : IBitLocker
{
    public static readonly Guid ProtectorId = Guid.Parse("4a5359b5-b088-431b-91e0-c915fee4f6b4");

    // Each group divisible by 11 and at most 720885, as BitLocker makes them.
    public const string Password = "123321-234432-345543-456654-567765-678876-112211-221122";

    public bool IsInstalled => true;

    public Task<bool> PreprovisionAsync(string volume, CancellationToken cancellationToken) => Said($"Dry run: {volume} is not encrypted.", true);

    public Task<bool> HasTpmProtectorAsync(string volume, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<IReadOnlyList<Guid>> RecoveryPasswordIdsAsync(string volume, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task DeleteProtectorAsync(string volume, Guid protectorId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> AddTpmProtectorAsync(string volume, CancellationToken cancellationToken) => Said($"Dry run: no TPM protector is added to {volume}.", true);

    public Task<RecoveryPassword?> AddRecoveryPasswordAsync(string volume, CancellationToken cancellationToken) =>
        Said<RecoveryPassword?>($"Dry run: no recovery password is added to {volume}; a made-up one stands in.", new RecoveryPassword(ProtectorId, Password));

    public Task BackupToActiveDirectoryAsync(string volume, Guid protectorId, CancellationToken cancellationToken) =>
        Said($"Dry run: the recovery password of {volume} is not backed up to Active Directory.", true);

    public Task TurnOnAsync(string volume, CancellationToken cancellationToken) => Said($"Dry run: BitLocker is not turned on for {volume}.", true);

    public Task<VolumeStatus> StatusAsync(string volume, CancellationToken cancellationToken) => Task.FromResult(new VolumeStatus(1, 100, true));

    private Task<T> Said<T>(string message, T result)
    {
        log.Information(message);

        return Task.FromResult(result);
    }
}

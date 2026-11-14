// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.BitLocker;

// BitLocker on a volume such as C:, through manage-bde. Any failure throws DeploymentStepException, whose message never
// holds a password. The methods that answer false or null do so when Windows finds no usable TPM.
public interface IBitLocker
{
    // manage-bde is there. Windows Server has it only with its BitLocker feature.
    bool IsInstalled { get; }

    // Starts encrypting the empty volume in Windows PE with a clear key.
    Task<bool> PreprovisionAsync(string volume, CancellationToken cancellationToken);

    Task<bool> HasTpmProtectorAsync(string volume, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> RecoveryPasswordIdsAsync(string volume, CancellationToken cancellationToken);

    Task DeleteProtectorAsync(string volume, Guid protectorId, CancellationToken cancellationToken);

    Task<bool> AddTpmProtectorAsync(string volume, CancellationToken cancellationToken);

    Task<RecoveryPassword?> AddRecoveryPasswordAsync(string volume, CancellationToken cancellationToken);

    Task BackupToActiveDirectoryAsync(string volume, Guid protectorId, CancellationToken cancellationToken);

    // Encrypts the used space and turns protection on once done. Right on a decrypted volume, one encrypting with a
    // clear key, and one already protected.
    Task TurnOnAsync(string volume, CancellationToken cancellationToken);

    Task<VolumeStatus> StatusAsync(string volume, CancellationToken cancellationToken);
}

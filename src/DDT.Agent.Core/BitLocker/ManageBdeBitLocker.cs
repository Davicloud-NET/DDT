// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Deployment;

namespace DDT.Agent.BitLocker;

// BitLocker through manage-bde, and its state through one PowerShell line. Output that holds a password stays out of
// the logs; every other line goes to the log unless it looks like a password. Every command line is here.
public sealed class ManageBdeBitLocker(IToolRunner tools, string manageBdePath, string powerShellPath) : IBitLocker
{
    // TBS_E_TPM_NOT_FOUND from -add -recoverypassword, and the error -add -tpm ends with when there is no TPM.
    public const int NoTpm = unchecked((int)0x8028400F);
    public const int NoCompatibleTpm = unchecked((int)0x80284008);

    // What manage-bde -get ends with when the volume has no such protector.
    public const int NoProtector = -1;

    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public bool IsInstalled => File.Exists(manageBdePath);

    public static string In(string systemDirectory) => Path.Combine(systemDirectory, "manage-bde.exe");

    public static IReadOnlyList<string> PreprovisionArguments(string volume) => ["-on", volume, "-used"];

    public static IReadOnlyList<string> TpmProtectorArguments(string volume) => ["-protectors", "-get", volume, "-type", "TPM"];

    public static IReadOnlyList<string> RecoveryPasswordsArguments(string volume) => ["-protectors", "-get", volume, "-type", "RecoveryPassword"];

    public static IReadOnlyList<string> DeleteArguments(string volume, Guid id) => ["-protectors", "-delete", volume, "-id", Braced(id)];

    public static IReadOnlyList<string> AddTpmArguments(string volume) => ["-protectors", "-add", volume, "-tpm"];

    public static IReadOnlyList<string> AddRecoveryPasswordArguments(string volume) => ["-protectors", "-add", volume, "-recoverypassword"];

    public static IReadOnlyList<string> BackupArguments(string volume, Guid id) => ["-protectors", "-adbackup", volume, "-id", Braced(id)];

    // Without -skiphardwaretest, encryption waits for a restart.
    public static IReadOnlyList<string> TurnOnArguments(string volume) => ["-on", volume, "-used", "-skiphardwaretest"];

    // Numbers only, so no display language changes them.
    public static IReadOnlyList<string> StatusArguments(string volume) =>
    [
        "-NoProfile",
        "-NonInteractive",
        "-ExecutionPolicy",
        "Bypass",
        "-Command",
        "try { $v = Get-CimInstance -Namespace root/cimv2/Security/MicrosoftVolumeEncryption -ClassName Win32_EncryptableVolume "
            + $"-Filter \"DriveLetter='{volume}'\" -ErrorAction Stop; "
            + "$c = Invoke-CimMethod -InputObject $v -MethodName GetConversionStatus -ErrorAction Stop; "
            + "$p = Invoke-CimMethod -InputObject $v -MethodName GetProtectionStatus -ErrorAction Stop; "
            + "'status:' + $c.ConversionStatus + ' ' + $c.EncryptionPercentage + ' ' + $p.ProtectionStatus } "
            + "catch { [Console]::Error.WriteLine(($_ | Out-String).TrimEnd()); exit 1 }; exit 0",
    ];

    public async Task<bool> PreprovisionAsync(string volume, CancellationToken cancellationToken)
    {
        int exitCode = await RunAsync(PreprovisionArguments(volume), cancellationToken).ConfigureAwait(false);

        if (exitCode is NoTpm or NoCompatibleTpm)
        {
            return false;
        }

        Check(exitCode, "could not start encrypting", volume);

        return true;
    }

    public async Task<bool> HasTpmProtectorAsync(string volume, CancellationToken cancellationToken)
    {
        int exitCode = await RunAsync(TpmProtectorArguments(volume), cancellationToken).ConfigureAwait(false);

        if (exitCode == NoProtector)
        {
            return false;
        }

        Check(exitCode, "could not list the TPM protector", volume);

        return true;
    }

    // The listing prints each password, so it stays out of the logs.
    public async Task<IReadOnlyList<Guid>> RecoveryPasswordIdsAsync(string volume, CancellationToken cancellationToken)
    {
        PrivateOutput output = new();
        int exitCode = await tools.RunForExitCodeAsync(manageBdePath, RecoveryPasswordsArguments(volume), new ToolRunOptions(Timeout: Timeout, Private: output), cancellationToken)
            .ConfigureAwait(false);

        if (exitCode == NoProtector)
        {
            return [];
        }

        Check(exitCode, "could not list the recovery passwords", volume, logged: false);

        return RecoveryPasswords.ProtectorIds(output.Lines);
    }

    public async Task DeleteProtectorAsync(string volume, Guid protectorId, CancellationToken cancellationToken) =>
        Check(await RunAsync(DeleteArguments(volume, protectorId), cancellationToken).ConfigureAwait(false), $"could not delete protector {Braced(protectorId)}", volume);

    public async Task<bool> AddTpmProtectorAsync(string volume, CancellationToken cancellationToken)
    {
        int exitCode = await RunAsync(AddTpmArguments(volume), cancellationToken).ConfigureAwait(false);

        if (exitCode is NoTpm or NoCompatibleTpm)
        {
            return false;
        }

        Check(exitCode, "could not add the TPM protector", volume);

        return true;
    }

    // manage-bde prints the new password twice, so its output stays out of the logs.
    public async Task<RecoveryPassword?> AddRecoveryPasswordAsync(string volume, CancellationToken cancellationToken)
    {
        PrivateOutput output = new();
        int exitCode = await tools.RunForExitCodeAsync(manageBdePath, AddRecoveryPasswordArguments(volume), new ToolRunOptions(Timeout: Timeout, Private: output), cancellationToken)
            .ConfigureAwait(false);

        if (exitCode is NoTpm or NoCompatibleTpm)
        {
            return null;
        }

        Check(exitCode, "could not add a recovery password", volume, logged: false);
        IReadOnlyList<string> passwords = RecoveryPasswords.Find(output.Lines);
        IReadOnlyList<Guid> ids = RecoveryPasswords.ProtectorIds(output.Lines);

        return passwords.Count == 1 && ids.Count == 1
            ? new RecoveryPassword(ids[0], passwords[0])
            : throw new DeploymentStepException(string.Create(
                CultureInfo.InvariantCulture,
                $"manage-bde added a recovery password to {volume}, but its answer held {passwords.Count} passwords and {ids.Count} protector IDs instead of one each."));
    }

    public async Task BackupToActiveDirectoryAsync(string volume, Guid protectorId, CancellationToken cancellationToken) =>
        Check(await RunAsync(BackupArguments(volume, protectorId), cancellationToken).ConfigureAwait(false), "could not back up the recovery password to Active Directory", volume);

    public async Task TurnOnAsync(string volume, CancellationToken cancellationToken) =>
        Check(await RunAsync(TurnOnArguments(volume), cancellationToken).ConfigureAwait(false), "could not turn BitLocker on", volume);

    public async Task<VolumeStatus> StatusAsync(string volume, CancellationToken cancellationToken)
    {
        // Private only to keep a long wait's polls out of the log.
        PrivateOutput output = new();
        int exitCode = await tools.RunForExitCodeAsync(powerShellPath, StatusArguments(volume), new ToolRunOptions(Timeout: Timeout, Private: output), cancellationToken)
            .ConfigureAwait(false);

        if (exitCode != 0)
        {
            string error = output.Errors.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? "no error text";

            throw new DeploymentStepException(string.Create(
                CultureInfo.InvariantCulture,
                $"PowerShell could not read the BitLocker state of {volume} (exit code {exitCode}): {RecoveryPasswords.Redacted(error) ?? "an error text that looks like a password"}"));
        }

        return ParseStatus(output.Lines) ?? throw new DeploymentStepException($"PowerShell did not say the BitLocker state of {volume}.");
    }

    public static VolumeStatus? ParseStatus(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        string[]? numbers = lines.FirstOrDefault(line => line.StartsWith("status:", StringComparison.Ordinal))?["status:".Length..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return numbers is [string conversion, string percent, string protection]
            && int.TryParse(conversion, NumberStyles.None, CultureInfo.InvariantCulture, out int c)
            && int.TryParse(percent, NumberStyles.None, CultureInfo.InvariantCulture, out int p)
            && int.TryParse(protection, NumberStyles.None, CultureInfo.InvariantCulture, out int protectionStatus)
                ? new VolumeStatus(c, p, protectionStatus == 1)
                : null;
    }

    private static string Braced(Guid id) => id.ToString("B").ToUpperInvariant();

    // Lines that look like a password never reach the log.
    private Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        tools.RunForExitCodeAsync(manageBdePath, arguments, new ToolRunOptions(Timeout: Timeout, Lines: RecoveryPasswords.Redacted), cancellationToken);

    private static void Check(int exitCode, string what, string volume, bool logged = true)
    {
        if (exitCode != 0)
        {
            throw new DeploymentStepException(
                $"manage-bde {what} on {volume} (exit code {ToolRunner.ExitCodeText(exitCode)}).{(logged ? " Its output is in the machine log." : string.Empty)}");
        }
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Deployment;

namespace DDT.Agent.WindowsFeatures;

// Enables a feature with DISM, the same way on Windows client and server. /All brings the features it needs along.
public sealed class DismWindowsFeatures(IToolRunner tools, string dismPath) : IWindowsFeatures
{
    // ERROR_SUCCESS_REBOOT_REQUIRED.
    public const int RestartNeeded = 3010;

    // CBS_E_UNKNOWN_UPDATE, which DISM ends with for a feature name it does not know.
    public const int UnknownFeature = unchecked((int)0x800F080C);

    // A feature such as NetFx3 downloads from Windows Update, which can be slow.
    public static readonly TimeSpan Timeout = TimeSpan.FromHours(1);

    public async Task<FeatureOutcome> EnableAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        int exitCode = await tools.RunForExitCodeAsync(
            dismPath,
            ["/Online", "/Enable-Feature", $"/FeatureName:{name}", "/All", "/NoRestart", "/Quiet"],
            new ToolRunOptions(Timeout: Timeout),
            cancellationToken).ConfigureAwait(false);

        return exitCode switch
        {
            0 => FeatureOutcome.Enabled,
            RestartNeeded => FeatureOutcome.RestartNeeded,
            UnknownFeature => FeatureOutcome.Unknown,
            _ => throw new DeploymentStepException(
                $"DISM could not enable {name} (exit code 0x{exitCode.ToString("X8", CultureInfo.InvariantCulture)}). Its log is %WINDIR%\\Logs\\DISM\\dism.log."),
        };
    }
}

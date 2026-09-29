// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// Registers the DdtSequence service straight into the installed Windows' SYSTEM hive from WinPE, so it starts whatever
// the image does with SetupComplete.cmd or its OEM keys. The hive is unloaded whatever happens, because a loaded hive
// stays locked.
public sealed class OfflineServiceRegistration(IToolRunner tools, AgentLog log, bool dryRun)
{
    public const string ServiceName = "DdtSequence";
    public const string HiveKey = @"HKLM\DDT_OFFLINE";
    // %SystemDrive% is the Windows volume once Windows runs.
    public const string ImagePath = "\"%SystemDrive%\\DDT\\agent\\ddt-agent.exe\" --service";
    public const string DisplayName = "DDT task sequence";
    public const string Description = "Runs the rest of a DDT task sequence in this Windows and removes itself when the run ends.";

    // SERVICE_FAILURE_ACTIONS as the service control manager stores it: the failure count resets after a day, no reboot
    // message, no command, and three actions at offset 20, each a restart after 60 seconds.
    public const string FailureActions =
        "80510100" + "00000000" + "00000000" + "03000000" + "14000000" +
        "0100000060ea0000" + "0100000060ea0000" + "0100000060ea0000";

    public static string RegPath => Path.Combine(Environment.SystemDirectory, "reg.exe");

    public static string HivePathIn(string windowsRoot) => Path.Combine(windowsRoot, "Windows", "System32", "config", "SYSTEM");

    public async Task RegisterAsync(string windowsRoot, CancellationToken cancellationToken)
    {
        string hive = HivePathIn(windowsRoot);
        await tools.RunAsync(RegPath, ["load", HiveKey, hive], cancellationToken).ConfigureAwait(false);

        bool registered = false;

        try
        {
            IReadOnlyList<string> select = await tools.RunAsync(RegPath, ["query", $@"{HiveKey}\Select", "/v", "Current"], cancellationToken)
                .ConfigureAwait(false);
            string key = $@"{HiveKey}\{ControlSet(select)}\Services\{ServiceName}";

            await AddAsync(key, "Type", "REG_DWORD", "16", cancellationToken).ConfigureAwait(false);
            await AddAsync(key, "Start", "REG_DWORD", "2", cancellationToken).ConfigureAwait(false);
            await AddAsync(key, "ErrorControl", "REG_DWORD", "1", cancellationToken).ConfigureAwait(false);
            await AddAsync(key, "ImagePath", "REG_EXPAND_SZ", ImagePath, cancellationToken).ConfigureAwait(false);
            await AddAsync(key, "ObjectName", "REG_SZ", "LocalSystem", cancellationToken).ConfigureAwait(false);
            await AddAsync(key, "DisplayName", "REG_SZ", DisplayName, cancellationToken).ConfigureAwait(false);
            await AddAsync(key, "Description", "REG_SZ", Description, cancellationToken).ConfigureAwait(false);
            await AddAsync(key, "FailureActions", "REG_BINARY", FailureActions, cancellationToken).ConfigureAwait(false);
            registered = true;
        }
        finally
        {
            try
            {
                await tools.RunAsync(RegPath, ["unload", HiveKey], CancellationToken.None).ConfigureAwait(false);
            }
            catch (DeploymentStepException exception) when (!registered)
            {
                // The failure that got here first is the one to report.
                log.Warning($"The installed Windows' SYSTEM hive could not be unloaded ({exception.Message}).");
            }
        }

        log.Information($"Registered the service {ServiceName} in {hive}. It starts the agent when Windows starts.");
    }

    private Task AddAsync(string key, string name, string type, string data, CancellationToken cancellationToken) =>
        tools.RunAsync(RegPath, ["add", key, "/v", name, "/t", type, "/d", data, "/f"], cancellationToken);

    // reg query prints the value as "    Current    REG_DWORD    0x1".
    private string ControlSet(IReadOnlyList<string> select)
    {
        foreach (string line in select)
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts is ["Current", "REG_DWORD", var value]
                && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int current)
                && current is > 0 and < 1000)
            {
                return string.Create(CultureInfo.InvariantCulture, $"ControlSet{current:D3}");
            }
        }

        // A dry run only logs the reg.exe lines, so no hive answers the query.
        if (dryRun)
        {
            log.Information("Dry run: no hive says which control set is current, so ControlSet001 stands in for it.");

            return "ControlSet001";
        }

        throw new DeploymentStepException(
            "The installed Windows' SYSTEM hive does not say which control set is current (Select\\Current), so the agent could not be " +
            "registered to go on with the run in Windows. Check that the image holds a complete Windows.");
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Globalization;

namespace DDT.Agent.Deployment;

// Restarts Windows PE with wpeutil, as the agent's exit leaves startnet.cmd's prompt open. Back into Windows PE it sets
// BootNext to BootCurrent, so the network starts next whatever the boot order says. Where the firmware made that entry
// for a one-time boot menu only, it warns and restarts plainly: the disk has no loader yet, so the firmware falls
// through to the network.
public sealed class WindowsPERebooter(IToolRunner tools, IUefiVariables variables, AgentLog log) : IRebooter
{
    private const string BootCurrent = "BootCurrent";
    private const string BootNext = "BootNext";

    public async Task RebootAsync(RestartInto into, CancellationToken cancellationToken)
    {
        if (into == RestartInto.WindowsPE)
        {
            StartNextFromThisEntry();
        }

        await tools.RunAsync(Path.Combine(Environment.SystemDirectory, "wpeutil.exe"), ["reboot"], cancellationToken).ConfigureAwait(false);
    }

    private const string Plainly =
        "The machine restarts without choosing where it starts next, so it comes back to Windows PE only if its boot order " +
        "starts from the network before the disk.";

    private void StartNextFromThisEntry()
    {
        try
        {
            if (variables.Read(BootCurrent) is not { Length: >= 2 } current)
            {
                log.Warning($"The firmware does not say which boot entry this start came from. {Plainly}");

                return;
            }

            string entry = string.Create(CultureInfo.InvariantCulture, $"Boot{BinaryPrimitives.ReadUInt16LittleEndian(current):X4}");

            if (variables.Read(entry) is null)
            {
                log.Warning($"The boot entry this start came from, {entry}, is gone, as after a one-time boot menu. {Plainly}");

                return;
            }

            variables.Write(BootNext, current[..2]);
            log.Information($"The next start comes from boot entry {entry} again, the one this start came from.");
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"The next start could not be set to this start's boot entry ({exception.Message}). {Plainly}");
        }
    }
}

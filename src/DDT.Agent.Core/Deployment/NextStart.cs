// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Globalization;
using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// Makes the firmware start next from the boot entry this start came from, whatever the boot order says, so a restart
// leads back into the boot environment. It sets BootNext to BootCurrent, and only warns where that cannot be done.
public sealed class NextStart(IUefiVariables variables, AgentLog log, AgentEnvironment environment)
{
    private const string BootCurrent = "BootCurrent";
    private const string BootNext = "BootNext";

    // A firmware may have made the entry only for a one-time boot menu. The restart is then plain.
    public void FromThisEntry()
    {
        string plainly = EnvironmentTexts.RestartsPlainly(environment);

        try
        {
            if (variables.Read(BootCurrent) is not { Length: >= 2 } current)
            {
                log.Warning($"The firmware does not say which boot entry this start came from. {plainly}");

                return;
            }

            string entry = string.Create(CultureInfo.InvariantCulture, $"Boot{BinaryPrimitives.ReadUInt16LittleEndian(current):X4}");

            if (variables.Read(entry) is null)
            {
                log.Warning($"The boot entry this start came from, {entry}, is gone, as after a one-time boot menu. {plainly}");

                return;
            }

            variables.Write(BootNext, current[..2]);
            log.Information($"The next start comes from boot entry {entry} again, the one this start came from.");
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"The next start could not be set to this start's boot entry ({exception.Message}). {plainly}");
        }
    }
}

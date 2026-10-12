// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Globalization;
using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// Names the boot entry the firmware starts next, whatever it does with its boot order: the one this start came from,
// so a restart leads back into the boot environment, or the first of the order, where a run put its disk. It sets
// BootNext, and only warns where that cannot be done.
public sealed class NextStart(IUefiVariables variables, AgentLog log, AgentEnvironment environment)
{
    private const string BootCurrent = "BootCurrent";
    private const string BootNext = "BootNext";
    private const string BootOrder = "BootOrder";

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

    // Names the first entry of the boot order, where a run put its disk, as the next start too. A firmware that tries
    // the network before its order at every start would otherwise come back here once for nothing.
    public void FromTheFirstEntry()
    {
        try
        {
            if (variables.Read(BootOrder) is not { Length: >= 2 } order)
            {
                FromTheBootOrder();

                return;
            }

            string entry = string.Create(CultureInfo.InvariantCulture, $"Boot{BinaryPrimitives.ReadUInt16LittleEndian(order):X4}");
            variables.Write(BootNext, order[..2]);
            log.Information($"The next start comes from boot entry {entry}, the first in the boot order, also where the firmware tries the network before it.");
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"The next start could not be set to the first entry of the boot order ({exception.Message}), so the firmware's own order decides.");
        }
    }

    // Forgets a next start an earlier restart had set, so the boot order decides again, where a run put its disk first.
    public void FromTheBootOrder()
    {
        try
        {
            if (variables.Read(BootNext) is null)
            {
                return;
            }

            variables.Delete(BootNext);
            log.Information("The firmware was still to start once from the entry an earlier restart had named. That is taken back, so the next start follows the boot order.");
        }
        catch (DeploymentStepException exception)
        {
            log.Warning(
                $"The firmware is still to start once from the entry an earlier restart had named, and that could not be taken back ({exception.Message}). " +
                "The start after that one follows the boot order.");
        }
    }
}

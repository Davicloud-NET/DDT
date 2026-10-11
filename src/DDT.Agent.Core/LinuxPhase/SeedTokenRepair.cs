// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;
using DDT.Core.CloudInit;

namespace DDT.Agent.LinuxPhase;

// Takes the run's token off a seed that still holds it. The script wipes it once the agent is installed, but it may
// have failed to, or the machine lost power right after the unit was enabled.
public sealed class SeedTokenRepair(IMountedSeeds seeds, AgentLog log)
{
    public const string Repaired =
        "The cloud-init seed still held the run's token, which the agent's script had not removed. It is overwritten and removed now.";

    public const string NotRepaired =
        "The cloud-init seed still holds the run's token, and the agent could not remove it either. The server takes that token only for this run.";

    // True when a token was found and is gone now.
    public bool Run()
    {
        bool repaired = false;

        seeds.Each(seed =>
        {
            string token = Path.Combine(seed, LinuxAgentFiles.SeedFolder, Path.GetFileName(LinuxAgentFiles.SeedToken));

            if (!File.Exists(token))
            {
                return;
            }

            Leftovers.Wipe(token, log);

            if (File.Exists(token))
            {
                log.Warning(NotRepaired);
            }
            else
            {
                log.Warning(Repaired);
                repaired = true;
            }
        });

        return repaired;
    }
}

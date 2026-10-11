// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.CloudInit;

// Where a run handed over to the installed Linux has its files: on the seed, and where the vendor-data script puts
// them. The hand-over, the script and the agent in the installed Linux share these names.
public static class LinuxAgentFiles
{
    // A folder of the seed, which cloud-init does not read.
    public const string SeedFolder = "ddt";
    public const string SeedAgent = SeedFolder + "/ddt-agent";
    public const string SeedConfiguration = SeedFolder + "/agent.json";
    public const string SeedState = SeedFolder + "/state.json";

    // The script finds the seed by this file, and wipes it once the agent is installed.
    public const string SeedToken = SeedFolder + "/token";

    // The program is apart from its state, where a system keeps programs that are its own.
    public const string ProgramFolder = "/usr/local/lib/ddt";
    public const string Agent = ProgramFolder + "/ddt-agent";

    // Only root reads it. The run's files are in its run folder, as on a Windows volume.
    public const string StateFolder = "/var/lib/ddt";
    public const string Configuration = StateFolder + "/agent.json";
    public const string State = StateFolder + "/run/state.json";
    public const string Token = StateFolder + "/run/token";

    public const string UnitName = "ddt-agent.service";
    public const string Unit = "/etc/systemd/system/" + UnitName;

    // What the unit starts the agent with.
    public const string Argument = "--linux-phase";

    // Where the script mounts the seed while it runs.
    public const string SeedMount = "/run/ddt-seed";

    // What the hand-over adds to the seed: the script cloud-init runs once, and what that installs.
    public static IReadOnlyList<SeedFile> ForSeed(byte[] agent, byte[] configuration, byte[] state, byte[] token)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(token);

        return
        [
            new(CloudInitSeed.VendorData, VendorDataScript.Bytes()),
            new(SeedAgent, agent),
            new(SeedConfiguration, configuration),
            new(SeedState, state),
            new(SeedToken, token),
        ];
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;
using DDT.Core.CloudInit;

namespace DDT.Agent.LinuxPhase;

// Where the agent in the installed Linux has its files, under root, which is / on a machine. The tests and a dry run
// give it a folder.
public sealed record LinuxAgentPaths(string Root)
{
    // On a file system in memory, which every start begins empty and which starts no program.
    public const string RunFolder = "/run/ddt";

    public static LinuxAgentPaths Machine { get; } = new("/");

    // Holds the run, the scripts and the packages, because /run starts no program.
    public string State => At(LinuxAgentFiles.StateFolder);

    public string Configuration => At(LinuxAgentFiles.Configuration);

    public string LogFile => Path.Combine(State, "logs", "agent.log");

    // Among the run's files, so it goes with them.
    public string CloudInitWaited => Path.Combine(RunFiles.DirectoryIn(State), "run", "cloud-init-waited");

    // Beside it, with the id of the start of Linux the agent first ran in.
    public string FirstStart => Path.Combine(RunFiles.DirectoryIn(State), "run", "cloud-init-first-start");

    public string Program => At(LinuxAgentFiles.ProgramFolder);

    public string Agent => At(LinuxAgentFiles.Agent);

    public string Unit => At(LinuxAgentFiles.Unit);

    public string Run => At(RunFolder);

    public string Shares => Path.Combine(Run, "shares");

    // The agent's own place for the seed. The script's is still mounted while the agent starts.
    public string SeedMount => Path.Combine(Run, "seed");

    // The console's socket, which the console's user must reach. The run folder is root's alone.
    public string ConsoleSockets => At("/run/ddt-console");

    // Where cloud-init keeps the script parts of the vendor-data it ran for this instance.
    public string VendorScripts => At("/var/lib/cloud/instance/scripts/vendor");

    public string At(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        // A folder on Windows stands in for the root in tests and dry runs, where a path has other separators.
        return Path.GetFullPath(Path.Combine(Root, path.TrimStart('/')));
    }
}

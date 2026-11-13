// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using DDT.Agent.Deployment;
using DDT.Agent.WindowsFeatures;
using DDT.Agent.WindowsUpdates;

namespace DDT.Agent.Sequences;

// The runners for every kind of step WinPE and the installed Windows run, from the machine's tools.
public sealed class WindowsStepRunners : IStepRunners
{
    public required IAgentServer Server { get; init; }

    public required IDiskPartitioner Partitioner { get; init; }

    public required IImageApplier Applier { get; init; }

    public required IToolRunner Tools { get; init; }

    public required IDomainJoiner Joiner { get; init; }

    public required AgentLog Log { get; init; }

    public required TimeProvider TimeProvider { get; init; }

    public required SequenceRunnerOptions Options { get; init; }

    // winget runs as it. Only the installed Windows has one, and a dry run stands in for it.
    public IDeployAccount? DeployAccount { get; init; }

    public IReadOnlyList<IStepKindRunner> For(StepRunnerParts run)
    {
        ArgumentNullException.ThrowIfNull(run);

        (RunSession session, FileRunStateStore store, RunHeartbeat heartbeat, RunDownloads downloads, _) = run;

        return
        [
            new PartitionStepRunner(Partitioner, session, store, Log, Options.DryRun),
            new ApplyImageStepRunner(Applier, downloads, session, Log),
            new InjectDriversStepRunner(Tools, downloads, session, Log),
            new WriteUnattendStepRunner(Server, session, heartbeat.ReportNowAsync, Log, TimeProvider),
            new JoinDomainStepRunner(Joiner, Server, session, heartbeat.ReportNowAsync, Log, TimeProvider),
            new RunScriptStepRunner(Tools, new WindowsScriptLauncher(), downloads, session, Log, Options.WorkDirectory),
            new PauseStepRunner(heartbeat, run.Console, Log, TimeProvider),
            new InstallApplicationsStepRunner(Server, session, heartbeat.ReportNowAsync, Applications(session, downloads), Log, TimeProvider),
            new EnableFeaturesStepRunner(Options.DryRun ? new DryRunWindowsFeatures(Log) : new DismWindowsFeatures(Tools, InjectDriversStepRunner.DismPath), Log),
            new WindowsUpdateStepRunner(Updates(), Log, TimeProvider),
        ];
    }

    private IWindowsUpdate Updates() =>
        Options.DryRun ? new DryRunWindowsUpdate(Log, Path.Combine(Options.WorkDirectory, "dry-run-updates.txt")) : new UnbuiltWindowsUpdate();

    // A dry run finds no application installed, whatever this computer has.
    private ApplicationInstaller Applications(RunSession session, RunDownloads downloads) => new(
        Options.DryRun ? new DryRunApplicationDetector(Log) : WindowsApplicationDetector.ForThisMachine(),
        Kinds(),
        new ApplicationFolders(downloads, session, Options.WorkDirectory),
        Log);

    private List<IApplicationKindInstaller> Kinds()
    {
        // A dry run takes DISM's word, whatever this computer has provisioned.
        List<IApplicationKindInstaller> kinds =
        [
            new CommandApplicationInstaller(Tools, Log),
            new MsixApplicationInstaller(Tools, Options.DryRun ? null : new RegistryProvisionedPackages(), Log, TimeProvider),
        ];

        if (DeployAccount is { } account)
        {
            kinds.Add(new WingetApplicationInstaller(Tools, account, Log, TimeProvider));
        }

        return kinds;
    }
}

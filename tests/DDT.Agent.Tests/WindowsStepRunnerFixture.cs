// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Agent.WindowsFeatures;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Tests;

// StepRunnerFixture with the runners of the steps WinPE and the installed Windows run, and the accounts of a step.
internal sealed class WindowsStepRunnerFixture(
    IReadOnlyList<SequenceStep> steps,
    IReadOnlyList<AgentRunImage>? images = null,
    IReadOnlyList<AgentRunPackage>? packages = null,
    FixtureSecureBoot? secureBoot = null,
    IReadOnlyList<VariableDeclaration>? variables = null)
    : StepRunnerFixture(steps, images, packages, secureBoot, variables, new DriveFreeSpace())
{
    public PartitionStepRunner Partition => new(Tools, Session, Store, Log, dryRun: true);

    public ApplyImageStepRunner ApplyImage => new(Tools, Downloads, Session, Log);

    public InjectDriversStepRunner InjectDrivers => new(ToolRunner, Downloads, Session, Log);

    public WriteUnattendStepRunner WriteUnattend => new(Server, Session, ReportRunningAsync, Log, Time);

    public JoinDomainStepRunner JoinDomain => new(Tools, Server, Session, ReportRunningAsync, Log, Time);

    public RunScriptStepRunner RunScript => new(ToolRunner, new WindowsScriptLauncher(), Downloads, Session, Log, WorkDirectory);

    // Command applications through ToolRunner, and a dry run's detector.
    public InstallApplicationsStepRunner InstallApplications => new(
        Server,
        Session,
        ReportRunningAsync,
        new ApplicationInstaller(
            new DryRunApplicationDetector(Log),
            [new CommandApplicationInstaller(ToolRunner, Log)],
            new ApplicationFolders(Downloads, Session, WorkDirectory),
            Log),
        Log,
        Time);

    // DISM through ToolRunner.
    public EnableFeaturesStepRunner EnableFeatures => new(new DismWindowsFeatures(ToolRunner, InjectDriversStepRunner.DismPath), Log);

    // Stands in for signing accounts in and connecting shares.
    public FakeAccountTools Accounts { get; } = new();

    public StepAccounts StepAccounts => new(Server, Session, ReportRunningAsync, Accounts.Tools, Log, Time);

    public AgentStepRunner Steps =>
        new([Partition, ApplyImage, InjectDrivers, WriteUnattend, JoinDomain, RunScript, InstallApplications, EnableFeatures], StepAccounts, TokenRejections.Add, Log, Time);
}

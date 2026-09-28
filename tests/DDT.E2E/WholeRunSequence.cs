// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;

namespace DDT.E2E;

// The steps of the whole run, in the sequence's order: both phases, a restart in each, a skipped and a failing step,
// and a script from a package.
internal sealed class WholeRunSequence(DryRunLab lab)
{
    public PartitionStep Partition { get; } = DryRunTests.Partition();

    public RunScriptStep Phase { get; } = DryRunTests.Script("Show the phase", SequencePhase.WindowsPE, "echo %DDT_PHASE% %DDT_WINDOWS%");

    public RunScriptStep Skipped { get; } = DryRunTests.Script("Only on another model", SequencePhase.WindowsPE, "echo not here") with
    {
        Conditions = [new StepCondition(MachineVariableNames.Model, ConditionOperator.Equals, "Another model")],
    };

    // A dry run takes every script's exit code as 0, so this one fails by accepting only 1.
    public RunScriptStep Failing { get; } = DryRunTests.Script("Fails, and the run goes on", SequencePhase.WindowsPE, "exit /b 1") with
    {
        ContinueOnError = true,
        SuccessExitCodes = [1],
    };

    public RebootStep RestartWindowsPE { get; } = new() { Id = Guid.CreateVersion7(), Name = "Restart Windows PE" };

    public RunScriptStep AfterRestart { get; } = DryRunTests.Script("After the restart", SequencePhase.WindowsPE, "echo again");

    public ApplyImageStep Apply { get; } = new() { Id = Guid.CreateVersion7(), Name = "Apply the image", ImageId = lab.Image.Id };

    public InjectDriversStep Drivers { get; } = new() { Id = Guid.CreateVersion7(), Name = "Add drivers", RequireMatch = true };

    public WriteUnattendStep Unattend { get; } =
        new() { Id = Guid.CreateVersion7(), Name = "Write the answer file", LocalAdministrator = true };

    public RunScriptStep PowerShell { get; } =
        DryRunTests.Script("PowerShell in Windows", SequencePhase.Windows, "Write-Output $env:DDT_PHASE", ScriptInterpreter.PowerShell);

    public RebootStep RestartWindows { get; } = new() { Id = Guid.CreateVersion7(), Name = "Restart Windows" };

    public RunScriptStep Packaged { get; } =
        DryRunTests.Script("Script from a package", SequencePhase.Windows, "type readme.txt") with { PackageId = lab.Files.Id };

    public JoinDomainStep Join { get; } = new() { Id = Guid.CreateVersion7(), Name = "Join the domain" };

    public IReadOnlyList<SequenceStep> Steps =>
        [
            Partition, Phase, Skipped, Failing, RestartWindowsPE, AfterRestart, Apply, Drivers, Unattend, PowerShell, RestartWindows,
            Packaged, Join,
        ];
}

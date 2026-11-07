// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// A lean boot image may lack DISM or PowerShell. The installed Windows brings its own PowerShell. systemDirectory is
// where WinPE keeps both.
public sealed class BootImageToolsCheck(string systemDirectory) : IPreflightCheck
{
    public bool AfterDiskChoice => false;

    public void Check(IReadOnlyList<SequenceStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        if (steps.OfType<InjectDriversStep>().Any() && !File.Exists(InjectDriversStepRunner.DismIn(systemDirectory)))
        {
            throw new DeploymentStepException(InjectDriversStepRunner.NoDismMessage);
        }

        if (steps.OfType<RunScriptStep>().Any(step => step is { Interpreter: ScriptInterpreter.PowerShell, Phase: SequencePhase.WindowsPE })
            && !File.Exists(WindowsScriptLauncher.PowerShellIn(systemDirectory)))
        {
            throw new DeploymentStepException(WindowsScriptLauncher.NoPowerShellMessage);
        }
    }
}

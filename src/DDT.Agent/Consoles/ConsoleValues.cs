// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Images;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Consoles;

// The agent's values in the console protocol's terms. The protocol has types of its own, so the console needs neither
// DDT.Contracts nor the agent.
public static class ConsoleValues
{
    public static MicrosoftUefiCas? ToConsole(UefiCa? cas) => cas is { } known
        ? (known.HasFlag(UefiCa.Microsoft2011) ? MicrosoftUefiCas.Ca2011 : MicrosoftUefiCas.None)
            | (known.HasFlag(UefiCa.Microsoft2023) ? MicrosoftUefiCas.Ca2023 : MicrosoftUefiCas.None)
        : null;

    public static UefiCa? ToUefiCa(MicrosoftUefiCas? cas) => cas is { } known
        ? (known.HasFlag(MicrosoftUefiCas.Ca2011) ? UefiCa.Microsoft2011 : UefiCa.None)
            | (known.HasFlag(MicrosoftUefiCas.Ca2023) ? UefiCa.Microsoft2023 : UefiCa.None)
        : null;

    public static ConsoleLogLevel ToConsole(AgentLogLevel level) => level switch
    {
        AgentLogLevel.Warning => ConsoleLogLevel.Warning,
        AgentLogLevel.Error => ConsoleLogLevel.Error,
        _ => ConsoleLogLevel.Information,
    };

    public static ConsoleStepState ToConsole(StepState state) => state switch
    {
        StepState.Running => ConsoleStepState.Running,
        StepState.Done => ConsoleStepState.Done,
        StepState.Skipped => ConsoleStepState.Skipped,
        StepState.Failed => ConsoleStepState.Failed,
        _ => ConsoleStepState.Pending,
    };

    public static ConsolePhase ToConsole(SequencePhase phase) =>
        phase == SequencePhase.Windows ? ConsolePhase.Windows : ConsolePhase.WindowsPE;

    public static ConsoleActivity ToConsole(RunActivity activity) => activity switch
    {
        RunActivity.Step => ConsoleActivity.Step,
        RunActivity.HandingOver => ConsoleActivity.HandingOver,
        RunActivity.Restarting => ConsoleActivity.Restarting,
        RunActivity.WaitingForWindowsSetup => ConsoleActivity.WaitingForWindowsSetup,
        RunActivity.Finishing => ConsoleActivity.Finishing,
        RunActivity.Removing => ConsoleActivity.Removing,
        _ => ConsoleActivity.Preparing,
    };

    // The step's kind as SequenceStep's JSON names it. A kind this list does not know yet shows as "step".
    public static string KindOf(SequenceStep step) => step switch
    {
        PartitionStep => "partition",
        ApplyImageStep => "applyImage",
        InjectDriversStep => "injectDrivers",
        WriteUnattendStep => "writeUnattend",
        JoinDomainStep => "joinDomain",
        RunScriptStep => "runScript",
        RebootStep => "reboot",
        WriteRawImageStep => "writeRawImage",
        WriteCloudInitSeedStep => "writeCloudInitSeed",
        GroupStep => "group",
        IfStep => "if",
        RepeatStep => "repeat",
        SetVariableStep => "setVariable",
        PauseStep => "pause",
        _ => "step",
    };
}

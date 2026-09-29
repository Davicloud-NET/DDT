// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Accounts;
using DDT.Contracts.Sequences;

namespace DDT.E2E;

// The sequence of the tree dry run. It has an IF on the model, a repeat whose first try restarts WinPE and fails, the
// hand-over, a pause in Windows, and a script that runs as a stored account with a share connected as that account.
internal sealed class TreeSequence
{
    // Set variable can't add numbers, so the count of tries is written out. It's 0, then 0+1 on the first try and 0+1+1
    // on the second.
    public const string Tries = "Tries";
    private const string FirstTryCount = "0+1";

    public const string ShareHost = "files.e2e.ddt.test";
    public const string ShareUser = @"E2E\svc-share";

    private static readonly AllCondition s_lastStepWorked = new()
    {
        Parts =
        [
            new TestCondition(MachineVariableNames.LastStepFailed, ConditionOperator.Equals, "No"),
            new TestCondition(MachineVariableNames.LastExitCode, ConditionOperator.Equals, "0"),
        ],
    };

    public TreeSequence(DryRunLab lab, AccountView account)
    {
        Account = account;
        AccountReference stored = new(account.Id, null);
        TestCondition onFirstTry = new(Tries, ConditionOperator.Equals, FirstTryCount);

        Partition = DryRunTests.Partition();
        ApplyLab = new() { Id = Guid.CreateVersion7(), Name = "Apply the lab's image", ImageId = lab.Image.Id };
        NameForLab = new()
        {
            Id = Guid.CreateVersion7(),
            Name = "Name it for the lab",
            Variable = MachineVariableNames.ComputerName,
            Value = "LAB-{{SerialNumber|right:5}}",
        };
        ApplyOther = new() { Id = Guid.CreateVersion7(), Name = "Apply the other image", ImageId = lab.OtherImage.Id };
        ByModel = new()
        {
            Id = Guid.CreateVersion7(),
            Name = "By model",
            Test = new TestCondition(MachineVariableNames.FriendlyModel, ConditionOperator.Equals, DryRunLab.Model),
            Then = [ApplyLab, NameForLab],
            Else = [ApplyOther],
        };

        // A dry run takes every script's exit code as 0, so the first try fails by accepting only 1, and the second
        // works. The count decides which try each script runs on.
        Count = new() { Id = Guid.CreateVersion7(), Name = "Count the try", Variable = Tries, Value = "{{Tries}}+1" };
        Restart = new() { Id = Guid.CreateVersion7(), Name = "Restart before the first try", When = onFirstTry };
        FirstTry = DryRunTests.Script("Install the tool", SequencePhase.WindowsPE, "setup.exe /quiet") with
        {
            When = onFirstTry,
            SuccessExitCodes = [1],
            ContinueOnError = true,
        };
        SecondTry = DryRunTests.Script("Install the tool again", SequencePhase.WindowsPE, "setup.exe /quiet") with
        {
            When = new TestCondition(Tries, ConditionOperator.NotEquals, FirstTryCount),
        };
        UntilInstalled = new()
        {
            Id = Guid.CreateVersion7(),
            Name = "Until the tool is installed",
            Steps = [Count, Restart, FirstTry, SecondTry],
            Until = s_lastStepWorked,
            MaxTimes = 3,
        };

        Unattend = new() { Id = Guid.CreateVersion7(), Name = "Write the answer file", LocalAdministrator = true };
        InWindows = DryRunTests.Script("First in Windows", SequencePhase.Windows, "echo %DDT_PHASE%");
        Pause = new() { Id = Guid.CreateVersion7(), Name = "Check the machine", Message = "Check {{ComputerName}} before the drivers are copied." };
        Copy = DryRunTests.Script("Copy the drivers", SequencePhase.Windows, $@"robocopy \\{ShareHost}\drivers C:\Drivers /e") with
        {
            Shares = [new ShareConnection($@"\\{ShareHost}\drivers", stored)],
            RunAs = stored,
        };
    }

    public AccountView Account { get; }

    public PartitionStep Partition { get; }

    public ApplyImageStep ApplyLab { get; }

    public SetVariableStep NameForLab { get; }

    public ApplyImageStep ApplyOther { get; }

    public IfStep ByModel { get; }

    public SetVariableStep Count { get; }

    public RebootStep Restart { get; }

    public RunScriptStep FirstTry { get; }

    public RunScriptStep SecondTry { get; }

    public RepeatStep UntilInstalled { get; }

    public WriteUnattendStep Unattend { get; }

    public RunScriptStep InWindows { get; }

    public PauseStep Pause { get; }

    public RunScriptStep Copy { get; }

    public SequenceDefinition Definition =>
        new(SequenceDefinition.CurrentVersion, [Partition, ByModel, UntilInstalled, Unattend, InWindows, Pause, Copy])
        {
            Variables =
            [
                new VariableDeclaration { Name = MachineVariableNames.ComputerName, Default = "PC-{{SerialNumber|right:5}}", SetBySteps = true },
                new VariableDeclaration { Name = Tries, Default = "0", SetBySteps = true },
            ],
        };
}

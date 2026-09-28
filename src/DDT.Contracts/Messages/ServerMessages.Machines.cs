// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // Machines.

    public static readonly MessageTemplate MachineInState = Define("machine.inState", "The machine is {state}.");

    public static readonly MessageTemplate MachineNobodySignedIn = Define(
        "machine.nobodySignedIn",
        "Nobody has signed in at this machine yet.");

    public static readonly MessageTemplate MachineChangedWhileDeciding = Define(
        "machine.changedWhileDeciding",
        "The machine changed while this decision was made. Look at it again before deciding.");

    public static readonly MessageTemplate MachineChangedWhileAssigning = Define(
        "machine.changedWhileAssigning",
        "The machine changed while the sequence was assigned. Look at it again before assigning.");

    public static readonly MessageTemplate MachineRunChangedWhileStopping = Define(
        "machine.runChangedWhileStopping",
        "The run changed while it was being stopped. Look at the machine again.");

    public static readonly MessageTemplate MachineCannotBeRemoved = Define(
        "machine.cannotBeRemoved",
        "Only a rejected machine, or a waiting machine that was never approved and has no assigned run, can be removed.");

    public static readonly MessageTemplate MachineChangedWhileRemoving = Define(
        "machine.changedWhileRemoving",
        "A machine changed while it was being removed. Look at it again before removing it.");

    // Which sequence a machine gets, and why.

    public static readonly MessageTemplate ResolutionChosenAtMachine = Define(
        "resolution.chosenAtMachine",
        "{by} chose {sequence} at the machine, which comes before every rule.");

    public static readonly MessageTemplate ResolutionAssignedOnWeb = Define(
        "resolution.assignedOnWeb",
        "{by} assigned {sequence} on the web, which comes before every rule.");

    public static readonly MessageTemplate ResolutionNoRule = Define(
        "resolution.noRule",
        "No rule matches the MAC addresses or the model of this machine, so an operator chooses its sequence.");

    public static readonly MessageTemplate ResolutionRuleChooses = Define(
        "resolution.ruleChooses",
        "The rule for {rule} chooses {sequence}. A rule only chooses: the machine still needs an approval on the web, or someone who " +
        "signs in at it, where the sequence is offered.");

    public static readonly MessageTemplate ResolutionCannotRun = Define(
        "resolution.cannotRun",
        "{explanation} {count, plural, one {{sequence} has # problem, so it cannot run until it is fixed.} " +
        "other {{sequence} has # problems, so it cannot run until they are fixed.}}");

    // Assigning a sequence, approving with a rule's sequence, and ending a run.

    public static readonly MessageTemplate DeploymentMachineRunning = Define(
        "deployment.machineRunning",
        "The machine is running a task sequence. Stop that run before assigning another sequence.");

    public static readonly MessageTemplate DeploymentMachineRejected = Define(
        "deployment.machineRejected",
        "The machine was rejected, so it cannot be given a sequence. Assign the sequence to another machine.");

    public static readonly MessageTemplate DeploymentMachineRetired = Define(
        "deployment.machineRetired",
        "The machine was retired, so it cannot be given a sequence. Assign the sequence to another machine.");

    public static readonly MessageTemplate DeploymentAlreadyHasRun = Define(
        "deployment.alreadyHasRun",
        "This machine already has a run. Cancel it before assigning another sequence.");

    public static readonly MessageTemplate DeploymentSequenceGone = Define(
        "deployment.sequenceGone",
        "The sequence no longer exists. Load the page again and choose another sequence.");

    public static readonly MessageTemplate DeploymentSequenceHasProblems = Define(
        "deployment.sequenceHasProblems",
        "{count, plural, one {{sequence} has a problem, so it cannot run. Fix it on the sequence's page first.} " +
        "other {{sequence} has # problems, so it cannot run. Fix them on the sequence's page first.}}");

    public static readonly MessageTemplate DeploymentSettingsHaveProblems = Define(
        "deployment.settingsHaveProblems",
        "The deployment settings have problems, so no run starts until an administrator fixes them on the settings page: {problems}");

    public static readonly MessageTemplate DeploymentErasesOneOfManyDisks = Define(
        "deployment.erasesOneOfManyDisks",
        "{sequence} erases a disk, and this machine has more than one. Sign in at it and choose the disk there.");

    public static readonly MessageTemplate DeploymentEnterComputerName = Define(
        "deployment.enterComputerName",
        "Enter a computer name. {use}");

    public static readonly MessageTemplate DeploymentJoinsDomainUnderName = Define(
        "deployment.joinsDomainUnderName",
        "The sequence joins the machine to the domain under this name.");

    public static readonly MessageTemplate DeploymentSeedNamesMachine = Define(
        "deployment.seedNamesMachine",
        "The sequence's cloud-init seed gives the machine this name.");

    public static readonly MessageTemplate DeploymentUntrustedCaWithSecureBoot = Define(
        "deployment.untrustedCaWithSecureBoot",
        "{image} is signed under {ca}, which this machine's firmware does not trust, and this machine has Secure Boot on. Allow it for " +
        "this run, or allow that CA or turn Secure Boot off in the machine's firmware first.");

    public static readonly MessageTemplate DeploymentNotStartingWithSecureBoot = Define(
        "deployment.notStartingWithSecureBoot",
        "{image} {starting, select, maybe {may not start} other {will not start}} with Secure Boot on, and this machine has Secure " +
        "Boot on. Allow it for this run, or turn Secure Boot off in the machine's firmware first.");

    public static readonly MessageTemplate DeploymentSignerChooses = Define(
        "deployment.signerChooses",
        "{signer} signed in at the machine and chooses its sequence there. Approve it without a sequence.");

    public static readonly MessageTemplate DeploymentRulesNoLongerChoose = Define(
        "deployment.rulesNoLongerChoose",
        "The rules no longer choose that sequence for this machine. {explanation} Look at the machine again.");

    public static readonly MessageTemplate DeploymentApproveThenChooseDisk = Define(
        "deployment.approveThenChooseDisk",
        "{sequence} erases a disk, and this machine has more than one. Approve it without a sequence, then sign in at it and choose " +
        "the disk there.");

    public static readonly MessageTemplate DeploymentApproveThenName = Define(
        "deployment.approveThenName",
        "{sequence} {use, select, domain {joins the domain} other {names the machine in its cloud-init seed}}, and this machine has no " +
        "name yet. Approve it without a sequence, then assign the sequence with a computer name.");

    public static readonly MessageTemplate DeploymentNothingToEnd = Define(
        "deployment.nothingToEnd",
        "This machine has no run that is assigned or running. Load the page again to see its current state.");

    public static readonly MessageTemplate DeploymentHistoryCursor = Define(
        "deployment.historyCursor",
        "The cursor is not one this server handed out. Start again from the first page.");

    // The answers to a run's inputs, a computer name a run's values give, and a name a sequence uses that nothing gives a
    // value.

    public static readonly MessageTemplate DeploymentAnswerNoInput = Define(
        "deployment.answerNoInput",
        "An answer names no input. Load the page again.");

    public static readonly MessageTemplate DeploymentAnswerUnknown = Define(
        "deployment.answerUnknown",
        "The sequence asks nothing called {name}. Load the page again.");

    public static readonly MessageTemplate DeploymentAnswerTwice = Define(
        "deployment.answerTwice",
        "{label} is answered twice. Give one answer.");

    public static readonly MessageTemplate DeploymentAnswerAskedElsewhere = Define(
        "deployment.answerAskedElsewhere",
        "{where, select, web {{label} is asked on the web, not at the machine.} other {{label} is asked at the machine, not on the web.}}");

    public static readonly MessageTemplate DeploymentAnswerTooLong = Define(
        "deployment.answerTooLong",
        "{label} takes at most {max} characters.");

    public static readonly MessageTemplate DeploymentAnswerNotAChoice = Define(
        "deployment.answerNotAChoice",
        "''{value}'' is not one of the choices of {label}.");

    public static readonly MessageTemplate DeploymentAnswerYesNo = Define(
        "deployment.answerYesNo",
        "{label} is answered with yes or no.");

    public static readonly MessageTemplate DeploymentApproveThenNameValue = Define(
        "deployment.approveThenNameValue",
        "{sequence} names the machine with its ComputerName value, and nothing gives this machine one yet. Approve it without a " +
        "sequence, then assign the sequence with a computer name.");

    public static readonly MessageTemplate SequenceValueUndefined = Define(
        "sequence.valueUndefined",
        "{name} is used, but neither the sequence nor a rule or a machine role gives it a value. A run fails where it needs it.");

    // Computer names, as the Windows answer file takes them.

    public static readonly MessageTemplate ComputerNameEmpty = Define("computerName.empty", "Enter a computer name.");

    public static readonly MessageTemplate ComputerNameTooLong = Define(
        "computerName.tooLong",
        "A computer name can have at most {max} characters.");

    public static readonly MessageTemplate ComputerNameCharacters = Define(
        "computerName.characters",
        "A computer name can hold only the letters A to Z, digits and hyphens.");

    public static readonly MessageTemplate ComputerNameDigitsOnly = Define(
        "computerName.digitsOnly",
        "A computer name cannot consist of digits only.");

    public static readonly MessageTemplate ComputerNameStartsWithHyphen = Define(
        "computerName.startsWithHyphen",
        "A computer name cannot start with a hyphen.");
}

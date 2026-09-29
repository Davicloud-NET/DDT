// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // Images a sequence writes.

    public static readonly MessageTemplate ImageRawForOtherArchitecture = Define(
        "image.rawForOtherArchitecture",
        "{image} starts {architecture} machines, and DDT writes images for x64 machines. Choose an x64 image.");

    public static readonly MessageTemplate ImageWithoutArchitecture = Define(
        "image.withoutArchitecture",
        "{image} does not say which processor it is for, and DDT deploys only x64 Windows. Choose an x64 image.");

    public static readonly MessageTemplate ImageOtherArchitecture = Define(
        "image.otherArchitecture",
        "{image} is an {architecture} image, and DDT deploys only x64 Windows. Choose an x64 image.");

    // The templates a new task sequence starts from.

    public static readonly MessageTemplate TemplateInstallWindows = Define("template.installWindows", "Install Windows");

    public static readonly MessageTemplate TemplateInstallWindowsDescription = Define(
        "template.installWindowsDescription",
        "Partitions the disk, applies an image, adds the drivers for the machine's model and writes the answer file.");

    public static readonly MessageTemplate TemplateInstallWindowsJoinDescription = Define(
        "template.installWindowsJoinDescription",
        "Partitions the disk, applies an image, adds the drivers for the machine's model and writes the answer file, then joins the " +
        "domain in Windows.");

    public static readonly MessageTemplate TemplateInstallLinux = Define("template.installLinux", "Install Linux");

    public static readonly MessageTemplate TemplateInstallLinuxDescription = Define(
        "template.installLinuxDescription",
        "Writes a raw disk image, such as a distribution's cloud image, and a cloud-init seed that names the machine.");

    // Task sequences: the requests that save them.

    public static readonly MessageTemplate SequenceSendJson = Define("sequence.sendJson", "Send the sequence as JSON.");

    public static readonly MessageTemplate SequenceUnreadable = Define(
        "sequence.unreadable",
        "The sequence is not a document DDT can read. Every step needs an id and a kind this version of DDT knows.");

    public static readonly MessageTemplate SequenceRequestTooLarge = Define(
        "sequence.requestTooLarge",
        "A sequence request can have at most {max} KiB.");

    public static readonly MessageTemplate SequenceNameTaken = Define(
        "sequence.nameTaken",
        "Another sequence is already called {name}. Choose another name.");

    public static readonly MessageTemplate SequenceChosenByRules = Define(
        "sequence.chosenByRules",
        "{count, plural, one {A rule chooses this sequence. Delete the rule or let it choose another sequence, then delete this one.} " +
        "other {# rules choose this sequence. Delete them or let them choose another sequence, then delete this one.}}");

    public static readonly MessageTemplate SequenceChangedWhileDeleting = Define(
        "sequence.changedWhileDeleting",
        "The sequence changed, or a rule chose it, while it was being deleted. Look at it again before deleting it.");

    // Task sequences: the problems that keep one from running, and the warnings.

    public static readonly MessageTemplate SequenceVersionUnsupported = Define(
        "sequence.versionUnsupported",
        "This version of DDT runs sequences of version 1 to {current}, not {version}.");

    public static readonly MessageTemplate SequenceVersionTooLow = Define(
        "sequence.versionTooLow",
        "The sequence has steps of version {required}, but says it is of version {version}.");

    public static readonly MessageTemplate SequenceStepCount = Define("sequence.stepCount", "A sequence needs 1 to {max} steps.");

    public static readonly MessageTemplate SequenceStepEmpty = Define("sequence.stepEmpty", "A step is empty.");

    public static readonly MessageTemplate SequenceStepWithoutId = Define("sequence.stepWithoutId", "Step {number} has no id.");

    public static readonly MessageTemplate SequenceStepIdRepeated = Define(
        "sequence.stepIdRepeated",
        "Step {number} has the id of an earlier step.");

    public static readonly MessageTemplate SequenceStepNameEmpty = Define("sequence.stepNameEmpty", "Enter a name for the step.");

    public static readonly MessageTemplate SequenceStepNameTooLong = Define(
        "sequence.stepNameTooLong",
        "A step name can have at most {max} characters.");

    public static readonly MessageTemplate SequenceWindowsPEAfterWindows = Define(
        "sequence.windowsPEAfterWindows",
        "Steps in Windows PE come first, and an earlier step already runs in Windows.");

    public static readonly MessageTemplate SequenceWindowsNeedsImage = Define(
        "sequence.windowsNeedsImage",
        "A step in Windows needs an earlier step that applies the image without conditions.");

    public static readonly MessageTemplate SequenceRestartAfterRestart = Define(
        "sequence.restartAfterRestart",
        "A restart step restarts anyway, so it cannot restart again after it.");

    public static readonly MessageTemplate SequenceRestartBeforePartition = Define(
        "sequence.restartBeforePartition",
        "Windows PE can restart only after the disk is partitioned, because the run's state is kept on the disk.");

    public static readonly MessageTemplate SequenceRestartWithRawImage = Define(
        "sequence.restartWithRawImage",
        "A sequence that writes a raw disk image keeps its state in memory, so Windows PE cannot restart during it.");

    public static readonly MessageTemplate SequencePackageWithRawImage = Define(
        "sequence.packageWithRawImage",
        "A sequence that writes a raw disk image has no partition to unpack a package on, so its scripts cannot have one.");

    public static readonly MessageTemplate SequenceWindowsWithRawImage = Define(
        "sequence.windowsWithRawImage",
        "A sequence either installs Windows or writes a raw disk image. This step belongs to installing Windows.");

    public static readonly MessageTemplate SequenceOneImage = Define("sequence.oneImage", "A sequence can apply only one image.");

    public static readonly MessageTemplate SequenceImageBeforePartition = Define(
        "sequence.imageBeforePartition",
        "The image can be applied only after a step that partitions the disk.");

    public static readonly MessageTemplate SequenceDriversBeforeImage = Define(
        "sequence.driversBeforeImage",
        "Drivers can be added only after the image is applied.");

    public static readonly MessageTemplate SequenceUnattendBeforeImage = Define(
        "sequence.unattendBeforeImage",
        "The answer file can be written only after the image is applied.");

    public static readonly MessageTemplate SequenceOneUnattend = Define(
        "sequence.oneUnattend",
        "A sequence can write the answer file only once.");

    public static readonly MessageTemplate SequenceOneDomainJoin = Define(
        "sequence.oneDomainJoin",
        "A sequence can join the domain only once.");

    public static readonly MessageTemplate SequenceOneRawImage = Define(
        "sequence.oneRawImage",
        "A sequence can write only one raw disk image.");

    public static readonly MessageTemplate SequenceSeedBeforeRawImage = Define(
        "sequence.seedBeforeRawImage",
        "The cloud-init seed can be written only after a step that writes a raw disk image.");

    public static readonly MessageTemplate SequenceOneSeed = Define(
        "sequence.oneSeed",
        "A sequence can write the cloud-init seed only once.");

    public static readonly MessageTemplate SequenceUnknownStep = Define(
        "sequence.unknownStep",
        "This version of DDT does not know this kind of step.");

    public static readonly MessageTemplate SequenceConditionsMissing = Define("sequence.conditionsMissing", "The conditions are missing.");

    public static readonly MessageTemplate SequenceTooManyConditions = Define(
        "sequence.tooManyConditions",
        "A step can have at most {max} conditions.");

    public static readonly MessageTemplate SequenceConditionEmpty = Define("sequence.conditionEmpty", "The condition is empty.");

    public static readonly MessageTemplate SequenceConditionVariable = Define(
        "sequence.conditionVariable",
        "Choose one of the variables {variables}.");

    public static readonly MessageTemplate SequenceConditionOperator = Define("sequence.conditionOperator", "Choose an operator.");

    public static readonly MessageTemplate SequenceConditionValue = Define("sequence.conditionValue", "Enter the value to compare with.");

    public static readonly MessageTemplate SequenceOnePartition = Define(
        "sequence.onePartition",
        "A sequence can partition the disk only once.");

    public static readonly MessageTemplate SequencePartitionAfterImage = Define(
        "sequence.partitionAfterImage",
        "The disk has to be partitioned before the image is applied.");

    public static readonly MessageTemplate SequenceSystemPartitionSize = Define(
        "sequence.systemPartitionSize",
        "The system partition needs {min} to {max} MB.");

    public static readonly MessageTemplate SequenceRecoveryPartitionSize = Define(
        "sequence.recoveryPartitionSize",
        "The recovery partition needs {min} to {max} MB.");

    public static readonly MessageTemplate SequenceCannotSkip = Define(
        "sequence.cannotSkip",
        "The sequence cannot skip {activity, select, partition {partitioning the disk} image {applying the image} " +
        "other {writing the raw disk image}}, so this step cannot have conditions.");

    public static readonly MessageTemplate SequenceCannotGoOn = Define(
        "sequence.cannotGoOn",
        "The sequence cannot go on when {activity, select, partition {partitioning the disk} image {applying the image} " +
        "other {writing the raw disk image}} fails.");

    public static readonly MessageTemplate SequenceSeedFileMissing = Define(
        "sequence.seedFileMissing",
        "The {file} file is missing. It can be empty.");

    public static readonly MessageTemplate SequenceSeedFileTooLarge = Define(
        "sequence.seedFileTooLarge",
        "The {file} file can have at most {max} KiB.");

    public static readonly MessageTemplate SequenceScriptPhase = Define("sequence.scriptPhase", "Choose Windows PE or Windows.");

    public static readonly MessageTemplate SequenceScriptInterpreter = Define("sequence.scriptInterpreter", "Choose cmd or PowerShell.");

    public static readonly MessageTemplate SequenceScriptEmpty = Define("sequence.scriptEmpty", "Enter the script.");

    public static readonly MessageTemplate SequenceScriptTooLarge = Define(
        "sequence.scriptTooLarge",
        "A script can have at most {max} KiB.");

    public static readonly MessageTemplate SequenceScriptTimeout = Define(
        "sequence.scriptTimeout",
        "The timeout has to be 1 to {max} minutes.");

    public static readonly MessageTemplate SequenceSuccessExitCodes = Define(
        "sequence.successExitCodes",
        "Enter 1 to {max} exit codes that mean success.");

    public static readonly MessageTemplate SequenceRebootExitCodes = Define(
        "sequence.rebootExitCodes",
        "Enter at most {max} exit codes that ask for a restart.");

    public static readonly MessageTemplate SequenceExitCodeMeansBoth = Define(
        "sequence.exitCodeMeansBoth",
        "An exit code cannot mean both success and a restart: {codes}.");

    public static readonly MessageTemplate SequencePackageBeforePartition = Define(
        "sequence.packageBeforePartition",
        "In Windows PE, a script with a package runs only after the disk is partitioned, where the package is put.");

    public static readonly MessageTemplate SequenceChooseImage = Define("sequence.chooseImage", "Choose the image to apply.");

    public static readonly MessageTemplate SequenceChooseRawImage = Define(
        "sequence.chooseRawImage",
        "Choose the raw disk image to write.");

    public static readonly MessageTemplate SequenceImageGone = Define(
        "sequence.imageGone",
        "The image is no longer in the library. Choose another image.");

    public static readonly MessageTemplate SequenceImageIsRaw = Define(
        "sequence.imageIsRaw",
        "{image} is a raw disk image, which a Write raw disk image step writes. Choose a Windows image.");

    public static readonly MessageTemplate SequenceImageIsWindows = Define(
        "sequence.imageIsWindows",
        "{image} is a Windows image, which an Apply image step applies. Choose a raw disk image.");

    public static readonly MessageTemplate SequenceRawImageNotStarting = Define(
        "sequence.rawImageNotStarting",
        "{image} {starting, select, maybe {may not start} other {will not start}} with Secure Boot on. {detail} Turn Secure Boot " +
        "off in the firmware of the machines it goes to, or enroll your own key. Assigning the sequence then asks to allow it.");

    public static readonly MessageTemplate SequenceUnknownPlaceholders = Define(
        "sequence.unknownPlaceholders",
        "{count, plural, one {{named} is not one of DDT's placeholders, so it stays as it is. DDT fills in {known}.} " +
        "other {{named} are not DDT's placeholders, so they stay as they are. DDT fills in {known}.}}");

    public static readonly MessageTemplate SequencePackageGone = Define(
        "sequence.packageGone",
        "The package is no longer in the library. Choose another package, or none.");

    public static readonly MessageTemplate SequencePackageIsDrivers = Define(
        "sequence.packageIsDrivers",
        "{package} is a driver package. A script runs with a files package.");

    public static readonly MessageTemplate SequenceTimeZone = Define(
        "sequence.timeZone",
        "''{timeZone}'' is not a Windows time zone id. Use a name that tzutil /l lists, such as W. Europe Standard Time.");

    public static readonly MessageTemplate SequenceLocale = Define(
        "sequence.locale",
        "''{locale}'' is not a language and region that Windows knows. Use a name such as de-DE.");

    public static readonly MessageTemplate SequenceKeyboard = Define(
        "sequence.keyboard",
        "''{keyboard}'' is not an input locale. Use a name such as de-DE or a code such as 0407:00000407.");

    public static readonly MessageTemplate SequenceNoLocalAdministrator = Define(
        "sequence.noLocalAdministratorSet",
        "No local administrator is set on the Deployment defaults page, so the answer file cannot add one.");

    public static readonly MessageTemplate SequenceNoDomain = Define(
        "sequence.noDomainSet",
        "No domain is set on the Deployment defaults page, so the machine has no domain to join. Set one there or remove this step.");

    public static readonly MessageTemplate SequenceNoAdministratorWarning = Define(
        "sequence.noAdministratorWarning",
        "The sequence continues in Windows, but no Write answer file step adds the local administrator. Windows setup then stops at " +
        "the account page, and the sequence waits there until someone finishes it.");
}

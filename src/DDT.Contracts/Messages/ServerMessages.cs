// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

// Every sentence the server says to a person on the web: refusals, validation messages and the texts of the pages'
// data, each with a stable code and its English text. The codes are part of the API. The web says a code in the
// person's language from src/DDT.Web/src/lib/serverMessages.ts, which is written from this list (see the test
// TheWebCatalogIsCurrent), and falls back to the English the server sent beside it for a code it does not know. A
// code never changes its meaning: a sentence that says something else gets a new code. Server logs, audit details and
// what the agent says stay English and are not listed here.
//
// Fields are initialised in the order they are written, so the lists come first and every message is in this file.
public static class ServerMessages
{
    private static readonly Dictionary<string, MessageTemplate> s_byCode = new(StringComparer.Ordinal);

    private static readonly List<MessageTemplate> s_all = [];

    public static IReadOnlyList<MessageTemplate> All => s_all;

    public static MessageTemplate? Find(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return s_byCode.GetValueOrDefault(code);
    }

    // People and things named within other sentences.

    public static readonly MessageTemplate SomeOperator = Define("person.someOperator", "An operator");

    // A machine's state and a person's role, as the server names them within a sentence.

    public static readonly MessageTemplate MachineStatePending = Define("machineState.pending", "Pending");

    public static readonly MessageTemplate MachineStateApproved = Define("machineState.approved", "Approved");

    public static readonly MessageTemplate MachineStateDeploying = Define("machineState.deploying", "Deploying");

    public static readonly MessageTemplate MachineStateDone = Define("machineState.done", "Done");

    public static readonly MessageTemplate MachineStateFailed = Define("machineState.failed", "Failed");

    public static readonly MessageTemplate MachineStateRejected = Define("machineState.rejected", "Rejected");

    public static readonly MessageTemplate MachineStateRetired = Define("machineState.retired", "Retired");

    public static readonly MessageTemplate RoleAdministrator = Define("role.administrator", "Administrator");

    public static readonly MessageTemplate RoleOperator = Define("role.operator", "Operator");

    public static readonly MessageTemplate RoleViewer = Define("role.viewer", "Viewer");

    public static readonly MessageTemplate RoleAnAdministrator = Define("role.anAdministrator", "an Administrator");

    public static readonly MessageTemplate RoleAnOperator = Define("role.anOperator", "an Operator");

    public static readonly MessageTemplate RoleAViewer = Define("role.aViewer", "a Viewer");

    public static readonly MessageTemplate RuleForMac = Define("rule.forMac", "MAC address {mac}");

    public static readonly MessageTemplate RuleForModel = Define("rule.forModel", "model {manufacturer} {model}");

    public static readonly MessageTemplate RuleForModelOfAnyMaker = Define("rule.forModelOfAnyMaker", "model {model} of any maker");

    public static readonly MessageTemplate MicrosoftUefiCaName = Define(
        "uefiCa.name",
        "{cas, select, ca2011 {Microsoft's third-party UEFI CA 2011} ca2023 {Microsoft's third-party UEFI CA 2023} " +
        "both {Microsoft's third-party UEFI CAs 2011 and 2023} other {Microsoft's third-party UEFI CA}}");

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

    // Names and descriptions of the things in the library.

    public static readonly MessageTemplate NameLength = Define(
        "common.nameLength",
        "The name must have 1 to {max} characters and no control characters.");

    public static readonly MessageTemplate DescriptionLength = Define(
        "common.descriptionLength",
        "The description can have at most {max} characters.");

    public static readonly MessageTemplate MacEnterFull = Define(
        "mac.enterFull",
        "Enter a MAC address of 12 hex digits, such as 00:15:5D:01:02:03.");

    public static readonly MessageTemplate MacEnterPart = Define(
        "mac.enterPart",
        "Enter 1 to 12 hex digits of a MAC address, such as 00:15:5D.");

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
        "sequence.noLocalAdministrator",
        "No local administrator is configured in DDT:Deployment:LocalAdministrator, so the answer file cannot add one.");

    public static readonly MessageTemplate SequenceNoDomain = Define(
        "sequence.noDomain",
        "No domain is configured in DDT:Deployment:Domain, so the machine has no domain to join. Configure one or remove this step.");

    public static readonly MessageTemplate SequenceNoAdministratorWarning = Define(
        "sequence.noAdministratorWarning",
        "The sequence continues in Windows, but no Write answer file step adds the local administrator. Windows setup then stops at " +
        "the account page, and the sequence waits there until someone finishes it.");

    // Organizational units, for a Join the domain step and the domain check.

    public static readonly MessageTemplate OrganizationalUnitWithPrefix = Define(
        "organizationalUnit.withPrefix",
        "Must be a distinguished name without the LDAP:// prefix, such as OU=Workstations,DC=example,DC=com.");

    public static readonly MessageTemplate OrganizationalUnitIsComputers = Define(
        "organizationalUnit.isComputers",
        "The default Computers container is no organizational unit and cannot be named. Leave this empty to use it.");

    public static readonly MessageTemplate OrganizationalUnitNotDistinguished = Define(
        "organizationalUnit.notDistinguished",
        "''{value}'' is not a distinguished name. Write it like OU=Workstations,DC=example,DC=com.");

    // Assignment rules and the models they, and driver packages, match.

    public static readonly MessageTemplate RuleChooseKind = Define("rule.chooseKind", "Choose a rule by MAC address or by model.");

    public static readonly MessageTemplate RuleSequenceGone = Define(
        "rule.sequenceGone",
        "The sequence no longer exists. Choose another one.");

    public static readonly MessageTemplate RuleExists = Define(
        "rule.exists",
        "There is a rule for {rule} already. It chooses {sequence}; change that rule instead.");

    public static readonly MessageTemplate ModelEnter = Define("model.enter", "Enter the model as the machine reports it.");

    public static readonly MessageTemplate ModelNameLength = Define(
        "model.nameLength",
        "A name here has at most {max} characters and no control characters.");

    public static readonly MessageTemplate ModelWildcardLast = Define("model.wildcardLast", "Only the last character can be {wildcard}.");

    public static readonly MessageTemplate ModelManufacturerWhole = Define(
        "model.manufacturerWhole",
        "A manufacturer is matched whole, without {wildcard}.");

    public static readonly MessageTemplate ModelWildcardPrefix = Define(
        "model.wildcardPrefix",
        "Put at least {min} characters before {wildcard}, so it matches only one family of models.");

    public static readonly MessageTemplate ModelPlaceholder = Define(
        "model.placeholder",
        "{value} is what firmware reports when the field was never filled in. It says nothing about the machine.");

    // Packages and images in the library.

    public static readonly MessageTemplate PackageTargetsMissing = Define(
        "package.targetsMissing",
        "Send the list of targets, empty for none.");

    public static readonly MessageTemplate PackageFilesHaveNoTargets = Define(
        "package.filesHaveNoTargets",
        "A Files package is unpacked for the Run script steps that name it, not by the machine's model, so it has no targets.");

    public static readonly MessageTemplate PackageTooManyTargets = Define(
        "package.tooManyTargets",
        "A package can have at most {max} targets.");

    public static readonly MessageTemplate PackageTargetEmpty = Define("package.targetEmpty", "A target is empty.");

    public static readonly MessageTemplate PackageTargetTwice = Define("package.targetTwice", "{model} is a target twice.");

    public static readonly MessageTemplate PackageBootImageDriversOnly = Define(
        "package.bootImageDriversOnly",
        "Only a driver package can go into the Windows PE boot image.");

    public static readonly MessageTemplate PackageInUse = Define(
        "package.inUse",
        "Machines are waiting to install this package or are installing it. Cancel those runs or let them finish, then delete it.");

    public static readonly MessageTemplate PackageFileMissing = Define(
        "package.fileMissing",
        "The package's file is missing from the server's library. Upload it again.");

    public static readonly MessageTemplate ImageInUse = Define(
        "image.inUse",
        "Runs that are assigned or running use this image. Cancel them or let them finish, then delete it.");

    // Uploads of images and packages.

    public static readonly MessageTemplate UploadFileName = Define(
        "upload.fileName",
        "The file name must have 1 to {max} characters and no control characters.");

    public static readonly MessageTemplate UploadKind = Define("upload.kind", "Choose an image, a driver package or a files package.");

    public static readonly MessageTemplate UploadLength = Define(
        "upload.length",
        "The file must not be empty and must fit on the server's store volume.");

    public static readonly MessageTemplate UploadNoSpace = Define(
        "upload.noSpace",
        "The image store needs {required} free for this upload but has {available}. Free space on the server's store volume or " +
        "discard unfinished uploads, then try again.");

    public static readonly MessageTemplate UploadContentLength = Define(
        "upload.contentLength",
        "Send every chunk with a Content-Length header.");

    public static readonly MessageTemplate UploadChunkSize = Define(
        "upload.chunkSize",
        "A chunk holds 1 to {max} bytes. Send the file in smaller chunks.");

    public static readonly MessageTemplate UploadOffsetHeader = Define(
        "upload.offsetHeader",
        "Send the position of the chunk in the file in the Upload-Offset header.");

    public static readonly MessageTemplate UploadBeyondLength = Define(
        "upload.beyondLength",
        "This chunk ends past the end of the file. Upload the file this session was created for.");

    public static readonly MessageTemplate UploadComplete = Define(
        "upload.complete",
        "This upload is complete. Nothing more needs to be sent.");

    public static readonly MessageTemplate UploadChunkBusy = Define(
        "upload.chunkBusy",
        "Another request is using this upload. Wait a few seconds, then send the chunk again.");

    public static readonly MessageTemplate UploadOffsetMismatch = Define(
        "upload.offsetMismatch",
        "The server holds a different part of this file. Continue from the offset in the Upload-Offset header.");

    public static readonly MessageTemplate UploadRestarted = Define(
        "upload.restarted",
        "The server lost part of this upload. Send the file again from the start.");

    public static readonly MessageTemplate UploadCutOff = Define(
        "upload.cutOff",
        "The chunk ended before all of it arrived. Send it again.");

    public static readonly MessageTemplate UploadStalled = Define(
        "upload.stalled",
        "The chunk stopped arriving for too long. Send it again.");

    public static readonly MessageTemplate UploadDiskFull = Define(
        "upload.diskFull",
        "The image store is full. Free space on the server's store volume, then continue the upload.");

    public static readonly MessageTemplate UploadGone = Define(
        "upload.gone",
        "This upload no longer exists. Select the file again to upload it.");

    public static readonly MessageTemplate UploadBeingChecked = Define(
        "upload.beingChecked",
        "This upload is being checked or written to. Ask again in a few seconds.");

    public static readonly MessageTemplate UploadIncomplete = Define(
        "upload.incomplete",
        "Not all of the file has arrived. Continue the upload from the offset in the Upload-Offset header.");

    public static readonly MessageTemplate UploadFailed = Define(
        "upload.failed",
        "The upload could not be added to the library. Look at the server log, then complete the upload again.");

    public static readonly MessageTemplate UploadServerStopping = Define(
        "upload.serverStopping",
        "The server is stopping. Complete the upload again once it is back.");

    public static readonly MessageTemplate UploadInUse = Define("upload.inUse", "This upload is in use. Try again in a few seconds.");

    // Accounts, their passwords, second factors and API tokens.

    public static readonly MessageTemplate UserNameEmpty = Define("user.nameEmpty", "Enter a user name.");

    public static readonly MessageTemplate UserNameTooLong = Define("user.nameTooLong", "A user name can have at most {max} characters.");

    public static readonly MessageTemplate UserDisplayNameEmpty = Define(
        "user.displayNameEmpty",
        "Enter the name DDT shows for the account.");

    public static readonly MessageTemplate UserDisplayNameTooLong = Define(
        "user.displayNameTooLong",
        "A display name can have at most {max} characters.");

    public static readonly MessageTemplate UserEmail = Define(
        "user.email",
        "Enter an email address such as jane@corp.example, or leave it empty.");

    public static readonly MessageTemplate UserRole = Define("user.role", "Choose Administrator, Operator or Viewer.");

    public static readonly MessageTemplate UserNameTaken = Define("user.nameTaken", "There is an account named {name} already.");

    public static readonly MessageTemplate UserDirectoryProvidesNames = Define(
        "user.directoryProvidesNames",
        "The directory provides the display name and email address of {name}. Change them there; DDT takes them over at its next " +
        "sign-in.");

    public static readonly MessageTemplate UserRoleFromDirectoryGroups = Define(
        "user.roleFromDirectoryGroups",
        "The role of {name} comes from its directory groups through DDT:Ldap:GroupRoleMap, at each sign-in. Change its groups in the " +
        "directory, or the map.");

    public static readonly MessageTemplate UserRoleFromSingleSignOnGroups = Define(
        "user.roleFromSingleSignOnGroups",
        "The role of {name} comes from its single sign-on groups through DDT:Oidc:GroupRoleMap, at each sign-in. Change its groups at " +
        "the provider, or the map.");

    public static readonly MessageTemplate UserOwnAdministratorRole = Define(
        "user.ownAdministratorRole",
        "You cannot take the Administrator role from your own account. Another administrator can.");

    public static readonly MessageTemplate UserLastAdministrator = Define(
        "user.lastAdministrator",
        "{name} is the last enabled administrator. Make another account an administrator first.");

    public static readonly MessageTemplate UserOwnDisable = Define(
        "user.ownDisable",
        "You cannot disable your own account. Another administrator can.");

    public static readonly MessageTemplate UserOwnPassword = Define("user.ownPassword", "Change your own password on the Account page.");

    public static readonly MessageTemplate UserDirectoryPassword = Define(
        "user.directoryPassword",
        "{name} signs in with its directory password. Reset it in the directory.");

    public static readonly MessageTemplate UserSingleSignOnPassword = Define(
        "user.singleSignOnPassword",
        "{name} signs in through single sign-on and has no password in DDT.");

    public static readonly MessageTemplate UserOwnSecondFactor = Define(
        "user.ownSecondFactor",
        "Turn off your own second factor on the Account page.");

    public static readonly MessageTemplate UserOwnDelete = Define(
        "user.ownDelete",
        "You cannot delete your own account. Another administrator can.");

    public static readonly MessageTemplate UserChangedWhileSaving = Define(
        "user.changedWhileSaving",
        "The account changed while this was saved. Look at it again.");

    public static readonly MessageTemplate AccountPasswordInDirectory = Define(
        "account.passwordInDirectory",
        "This account is managed by the directory. Change the password there.");

    public static readonly MessageTemplate AccountNoPassword = Define(
        "account.noPassword",
        "This account signs in through single sign-on and has no password in DDT.");

    public static readonly MessageTemplate AccountCodeNotValidCheckTime = Define(
        "account.codeNotValidCheckTime",
        "That code is not valid. Check the time on the device generating it.");

    public static readonly MessageTemplate AccountCodeNotValid = Define("account.codeNotValid", "That code is not valid.");

    public static readonly MessageTemplate AccountNoExternalSignIn = Define(
        "account.noExternalSignIn",
        "No external sign in is in progress.");

    public static readonly MessageTemplate AccountApiTokenCannotChange = Define(
        "account.apiTokenCannotChange",
        "An API token cannot change the account it belongs to. Sign in on the web to do this.");

    public static readonly MessageTemplate TokenNameTaken = Define(
        "token.nameTaken",
        "You have a token of that name already. Choose another name, or revoke that token first.");

    public static readonly MessageTemplate TokenNoRole = Define("token.noRole", "Your account has no role, so it cannot have a token.");

    public static readonly MessageTemplate TokenRoleTooHigh = Define("token.roleTooHigh", "A token can do at most what you can, and you are {role}.");

    public static readonly MessageTemplate TokenLifetime = Define("token.lifetime", "A token lasts 1 to {max} days.");

    public static readonly MessageTemplate TokenOnlyOwnerRevokes = Define(
        "token.onlyOwnerRevokes",
        "Only the token's owner or an administrator can revoke it.");

    public static readonly MessageTemplate AuditRangeEnd = Define("audit.rangeEnd", "The end of the range must come after its start.");

    // What ASP.NET Core Identity refuses, in its own English, which DdtIdentityErrorDescriber gives it from here.

    public static readonly MessageTemplate IdentityDefaultError = Define("identity.defaultError", "An unknown failure has occurred.");

    public static readonly MessageTemplate IdentityConcurrencyFailure = Define(
        "identity.concurrencyFailure",
        "Optimistic concurrency failure, object has been modified.");

    public static readonly MessageTemplate IdentityPasswordMismatch = Define("identity.passwordMismatch", "Incorrect password.");

    public static readonly MessageTemplate IdentityInvalidToken = Define("identity.invalidToken", "Invalid token.");

    public static readonly MessageTemplate IdentityRecoveryCodeRedemptionFailed = Define(
        "identity.recoveryCodeRedemptionFailed",
        "Recovery code redemption failed.");

    public static readonly MessageTemplate IdentityLoginAlreadyAssociated = Define(
        "identity.loginAlreadyAssociated",
        "A user with this login already exists.");

    public static readonly MessageTemplate IdentityInvalidUserName = Define(
        "identity.invalidUserName",
        "Username ''{name}'' is invalid, can only contain letters or digits.");

    public static readonly MessageTemplate IdentityInvalidEmail = Define("identity.invalidEmail", "Email ''{email}'' is invalid.");

    public static readonly MessageTemplate IdentityDuplicateUserName = Define(
        "identity.duplicateUserName",
        "Username ''{name}'' is already taken.");

    public static readonly MessageTemplate IdentityDuplicateEmail = Define(
        "identity.duplicateEmail",
        "Email ''{email}'' is already taken.");

    public static readonly MessageTemplate IdentityInvalidRoleName = Define("identity.invalidRoleName", "Role name ''{role}'' is invalid.");

    public static readonly MessageTemplate IdentityDuplicateRoleName = Define(
        "identity.duplicateRoleName",
        "Role name ''{role}'' is already taken.");

    public static readonly MessageTemplate IdentityUserAlreadyHasPassword = Define(
        "identity.userAlreadyHasPassword",
        "User already has a password set.");

    public static readonly MessageTemplate IdentityUserLockoutNotEnabled = Define(
        "identity.userLockoutNotEnabled",
        "Lockout is not enabled for this user.");

    public static readonly MessageTemplate IdentityUserAlreadyInRole = Define(
        "identity.userAlreadyInRole",
        "User already in role ''{role}''.");

    public static readonly MessageTemplate IdentityUserNotInRole = Define("identity.userNotInRole", "User is not in role ''{role}''.");

    public static readonly MessageTemplate IdentityPasswordTooShort = Define(
        "identity.passwordTooShort",
        "Passwords must be at least {length} characters.");

    public static readonly MessageTemplate IdentityPasswordRequiresUniqueChars = Define(
        "identity.passwordRequiresUniqueChars",
        "Passwords must use at least {count} different characters.");

    public static readonly MessageTemplate IdentityPasswordRequiresNonAlphanumeric = Define(
        "identity.passwordRequiresNonAlphanumeric",
        "Passwords must have at least one non alphanumeric character.");

    public static readonly MessageTemplate IdentityPasswordRequiresDigit = Define(
        "identity.passwordRequiresDigit",
        "Passwords must have at least one digit ('0'-'9').");

    public static readonly MessageTemplate IdentityPasswordRequiresLower = Define(
        "identity.passwordRequiresLower",
        "Passwords must have at least one lowercase ('a'-'z').");

    public static readonly MessageTemplate IdentityPasswordRequiresUpper = Define(
        "identity.passwordRequiresUpper",
        "Passwords must have at least one uppercase ('A'-'Z').");

    // An error Identity made some other way, in its English.
    public static readonly MessageTemplate IdentityOther = Define("identity.other", "{description}");

    // The directory for sign-ins, and the domain Join the domain steps join.

    public static readonly MessageTemplate DirectoryEnterUserName = Define("directory.enterUserName", "Enter the user name to check.");

    public static readonly MessageTemplate DirectoryIncomplete = Define(
        "directory.incomplete",
        "The directory connection is not complete. Set DDT:Ldap:Host and DDT:Ldap:BaseDn.");

    public static readonly MessageTemplate DirectoryOff = Define(
        "directory.off",
        "Sign-in through a directory is off. Turn on DDT:Ldap:Enabled and set its connection first.");

    public static readonly MessageTemplate DirectoryBindRefused = Define(
        "directory.bindRefused",
        "The directory at {server} refused the bind account {bindDn}. Check DDT:Ldap:BindDn and its password.");

    public static readonly MessageTemplate DirectoryUnreachable = Define(
        "directory.unreachable",
        "The directory at {server} could not be reached: {detail}");

    public static readonly MessageTemplate DirectorySearchRefused = Define(
        "directory.searchRefused",
        "The directory at {server} refused the search under {baseDn}: {detail} Check DDT:Ldap:BaseDn.");

    public static readonly MessageTemplate DirectoryNoEntry = Define(
        "directory.noEntry",
        "No entry under {baseDn} matches {name} through DDT:Ldap:UserFilter, so a sign-in with it is refused.");

    public static readonly MessageTemplate DirectoryManyEntries = Define(
        "directory.manyEntries",
        "More than one entry under {baseDn} matches {name} through DDT:Ldap:UserFilter, so a sign-in with it is refused.");

    public static readonly MessageTemplate DirectoryLocalAccount = Define(
        "directory.localAccount",
        "{name} is a local account in DDT, so a sign-in with this name checks its DDT password and never asks the directory.");

    public static readonly MessageTemplate DirectorySingleSignOnAccount = Define(
        "directory.singleSignOnAccount",
        "{name} is a single sign-on account in DDT, so a sign-in with this name and a password is refused.");

    public static readonly MessageTemplate DirectoryNoImmutableId = Define(
        "directory.noImmutableId",
        "The entry has no {attribute} value to key the account on, so a sign-in is refused.");

    public static readonly MessageTemplate DirectoryAccountDisabled = Define(
        "directory.accountDisabled",
        "The DDT account {name} is disabled, so a sign-in is refused.");

    public static readonly MessageTemplate DirectoryNoMapNewAccount = Define(
        "directory.noMapNewAccount",
        "DDT:Ldap:GroupRoleMap is empty, so administrators set roles. A first sign-in makes an account without one, which reaches " +
        "nothing until an administrator gives it a role.");

    public static readonly MessageTemplate DirectoryNoMapNoRole = Define(
        "directory.noMapNoRole",
        "DDT:Ldap:GroupRoleMap is empty, so administrators set roles. The account has none yet, so it reaches nothing.");

    public static readonly MessageTemplate DirectoryNoMapRole = Define(
        "directory.noMapRole",
        "DDT:Ldap:GroupRoleMap is empty, so administrators set roles. The account has {role}.");

    public static readonly MessageTemplate DirectoryNoMappedGroup = Define(
        "directory.noMappedGroup",
        "{name} is in none of the groups DDT:Ldap:GroupRoleMap maps to a role, so a sign-in is refused.");

    public static readonly MessageTemplate DirectoryRoleFromGroup = Define("directory.roleFromGroup", "{name} gets {role} from {group}.");

    public static readonly MessageTemplate DirectoryRoleFromGroups = Define(
        "directory.roleFromGroups",
        "{name} is in {count} mapped groups and gets the highest role they give, {role} from {group}.");

    public static readonly MessageTemplate DirectoryLockedOut = Define(
        "directory.lockedOut",
        "{reason} The account is locked out for now, so a sign-in waits until the lockout ends.");

    public static readonly MessageTemplate DomainNotConfigured = Define(
        "domain.notConfigured",
        "No domain is configured. Set DDT:Deployment:Domain:Name and the join account on the server.");

    public static readonly MessageTemplate DomainNoJoinAccount = Define(
        "domain.noJoinAccount",
        "The join account is not configured. Set DDT:Deployment:Domain:UserName and Password on the server.");

    public static readonly MessageTemplate DomainCheckError = Define(
        "domain.checkError",
        "{controller} answered the check with an error: {detail}");

    public static readonly MessageTemplate DomainSignedIn = Define(
        "domain.signedIn",
        "Signed in to {controller} as {user} over {connection}.");

    public static readonly MessageTemplate DomainOtherDomain = Define(
        "domain.otherDomain",
        "{controller} serves the domain {namingContext}, not {domain} ({expected}). Correct DDT:Deployment:Domain:Name, or point " +
        "DDT:Deployment:Domain:Controller at a domain controller of that domain.");

    public static readonly MessageTemplate DomainControllerOf = Define(
        "domain.controllerOf",
        "{controller} is a domain controller of {domain}.");

    public static readonly MessageTemplate DomainNoComputersContainer = Define(
        "domain.noComputersContainer",
        "The default Computers container of {domain} was not found, or {user} may not read it.");

    public static readonly MessageTemplate DomainNoOrganizationalUnit = Define(
        "domain.noOrganizationalUnit",
        "{domain} has no organizational unit {organizationalUnit}, or {user} may not read it. Correct the organizational unit of the " +
        "Join the domain step, or DDT:Deployment:Domain:OrganizationalUnit when the step names none.");

    public static readonly MessageTemplate DomainMayCreate = Define(
        "domain.mayCreate",
        "{user} may create computer objects in {container}, as many as it needs.");

    public static readonly MessageTemplate DomainMayNotCreateInUnit = Define(
        "domain.mayNotCreateInUnit",
        "{user} may not create computer objects in {container}, and the machine account quota does not reach an organizational unit. " +
        "Delegate \"Create Computer objects\" on it to the account, for example with the Delegation of Control wizard of Active " +
        "Directory Users and Computers.");

    public static readonly MessageTemplate DomainQuotaUnknown = Define(
        "domain.quotaUnknown",
        "{user} may not create computer objects in {container} by a right of its own, and the domain's machine account quota could " +
        "not be read. Delegate \"Create Computer objects\" on the container to the account.");

    public static readonly MessageTemplate DomainQuotaUsed = Define(
        "domain.quotaUsed",
        "{user} may not create computer objects in {container} by a right of its own, and has used its machine account quota: " +
        "{created} of {quota} (ms-DS-MachineAccountQuota). Delegate \"Create Computer objects\" on the container to the account.");

    public static readonly MessageTemplate DomainQuotaLeft = Define(
        "domain.quotaLeft",
        "{user} may not create computer objects in {container} by a right of its own, so it joins within the domain's machine " +
        "account quota: {created} of {quota} used, {left} left. That also needs the user right \"Add workstations to domain\", which " +
        "Authenticated Users hold by default and this check cannot read. Delegate \"Create Computer objects\" on the container to the " +
        "account to join more.");

    public static readonly MessageTemplate DomainNoSuchAccount = Define(
        "domain.noSuchAccount",
        "{controller} knows no account {user}. Correct DDT:Deployment:Domain:UserName.");

    public static readonly MessageTemplate DomainWrongPassword = Define(
        "domain.wrongPassword",
        "{controller} did not accept the password of {user}. Correct DDT:Deployment:Domain:Password.");

    public static readonly MessageTemplate DomainLogonHours = Define(
        "domain.logonHours",
        "{user} may not sign in at this time of day (logon hours).");

    public static readonly MessageTemplate DomainLogonWorkstations = Define(
        "domain.logonWorkstations",
        "{user} may not sign in from the DDT server (Log On To workstations).");

    public static readonly MessageTemplate DomainPasswordExpired = Define(
        "domain.passwordExpired",
        "The password of {user} has expired. Give it a new one, in the domain and in DDT:Deployment:Domain:Password.");

    public static readonly MessageTemplate DomainAccountDisabled = Define("domain.accountDisabled", "{user} is disabled.");

    public static readonly MessageTemplate DomainAccountExpired = Define("domain.accountExpired", "{user} has expired.");

    public static readonly MessageTemplate DomainMustChangePassword = Define(
        "domain.mustChangePassword",
        "{user} has to change its password before it can sign in. Give it a new one, in the domain and in " +
        "DDT:Deployment:Domain:Password.");

    public static readonly MessageTemplate DomainLockedOut = Define("domain.lockedOut", "{user} is locked out.");

    public static readonly MessageTemplate DomainSignInRefused = Define(
        "domain.signInRefused",
        "{controller} did not accept the user name or password of {user}.");

    public static readonly MessageTemplate DomainSignInRefusedWithReason = Define(
        "domain.signInRefusedWithReason",
        "{controller} did not accept the user name or password of {user} (reason {reason}).");

    public static readonly MessageTemplate DomainUnreachable = Define(
        "domain.unreachable",
        "{controller} could not be reached over LDAP. If this server's DNS does not know {domain}, set DDT:Deployment:Domain:Controller " +
        "to a domain controller's name or address. The machines find their domain controller through their own DNS.");

    public static readonly MessageTemplate DomainUnreachableWithDetail = Define(
        "domain.unreachableWithDetail",
        "{controller} could not be reached over LDAP ({detail}). If this server's DNS does not know {domain}, set " +
        "DDT:Deployment:Domain:Controller to a domain controller's name or address. The machines find their domain controller " +
        "through their own DNS.");

    public static readonly MessageTemplate DomainNoSecureConnection = Define(
        "domain.noSecureConnection",
        "{controller} offers no LDAPS on port 636 that this server trusts, and on this operating system only LDAPS keeps the join " +
        "account's password secret during the check. Give the domain controllers a certificate, for example from Active Directory " +
        "Certificate Services, and trust its CA on this server. Joining does not depend on this check.");

    // Why an uploaded file cannot go into the library: WIM images, disk images and zip packages.

    public static readonly MessageTemplate WimNotAWim = Define("wim.notAWim", "This file is not a WIM image.");

    public static readonly MessageTemplate WimPipable = Define(
        "wim.pipable",
        "Pipable WIM files are not supported. Export the image into a regular WIM first.");

    public static readonly MessageTemplate WimVersion = Define(
        "wim.version",
        "This WIM file uses format version 0x{version}, which DDT does not support.");

    public static readonly MessageTemplate WimSplit = Define(
        "wim.split",
        "Split WIM files (.swm) are not supported. Export the image into a single WIM first.");

    public static readonly MessageTemplate WimCompressedList = Define(
        "wim.compressedList",
        "This WIM file stores its image list compressed, which DDT cannot read.");

    public static readonly MessageTemplate WimIncomplete = Define("wim.incomplete", "This WIM file is incomplete or damaged.");

    public static readonly MessageTemplate WimListTooLarge = Define("wim.listTooLarge", "The image list in this WIM file is too large.");

    public static readonly MessageTemplate WimListNotUtf16 = Define(
        "wim.listNotUtf16",
        "The image list in this WIM file is not UTF-16 text, which DDT cannot read.");

    public static readonly MessageTemplate WimListDamaged = Define("wim.listDamaged", "The image list in this WIM file is damaged.");

    public static readonly MessageTemplate WimEncrypted = Define(
        "wim.encrypted",
        "This image is encrypted (an ESD from Windows Update) and cannot be applied.");

    public static readonly MessageTemplate WimListMismatch = Define(
        "wim.listMismatch",
        "The image list in this WIM file does not match its header.");

    public static readonly MessageTemplate WimNoX64Image = Define("wim.noX64Image", "This WIM holds no x64 Windows image.");

    public static readonly MessageTemplate GptNoTable = Define(
        "gpt.noTable",
        "The file has no GUID partition table, so it is not a UEFI disk image. Upload a disk image such as a distribution's cloud image.");

    public static readonly MessageTemplate GptFourKilobyteSectors = Define(
        "gpt.fourKilobyteSectors",
        "The disk image is made for disks with 4 KiB sectors. DDT writes images for disks with 512-byte sectors, which is what " +
        "distributions publish.");

    public static readonly MessageTemplate GptDamaged = Define("gpt.damaged", "The GUID partition table of the disk image is damaged.");

    public static readonly MessageTemplate GptDamagedUsableRange = Define(
        "gpt.damagedUsableRange",
        "The GUID partition table of the disk image is damaged. Its usable range does not fit its own headers.");

    public static readonly MessageTemplate GptDamagedEndsInTable = Define(
        "gpt.damagedEndsInTable",
        "The GUID partition table of the disk image is damaged. The file ends inside the partition table.");

    public static readonly MessageTemplate GptDamagedHeader = Define(
        "gpt.damagedHeader",
        "The GUID partition table of the disk image is damaged. Its header has a revision or size DDT does not know.");

    public static readonly MessageTemplate GptDamagedEntries = Define(
        "gpt.damagedEntries",
        "The GUID partition table of the disk image is damaged. Its partition entries are laid out in a way DDT does not read.");

    public static readonly MessageTemplate GptDamagedPartitionOutside = Define(
        "gpt.damagedPartitionOutside",
        "The GUID partition table of the disk image is damaged. Partition {number} lies outside the usable sectors.");

    public static readonly MessageTemplate GptDamagedOverlap = Define(
        "gpt.damagedOverlap",
        "The GUID partition table of the disk image is damaged. Partitions {first} and {second} overlap.");

    public static readonly MessageTemplate GptIncomplete = Define(
        "gpt.incomplete",
        "The disk image holds {length} bytes, but its partitions reach to byte {end}. The file is incomplete.");

    public static readonly MessageTemplate UploadNotAnImage = Define(
        "upload.notAnImage",
        "This file is neither a WIM image nor a disk image with a GUID partition table. Upload a WIM or ESD file, or a disk image " +
        "such as a distribution's cloud image.");

    public static readonly MessageTemplate UploadConversionOutOfSpace = Define(
        "upload.conversionOutOfSpace",
        "The store volume ran out of space while the image was converted. Free some space and complete the upload again.");

    public static readonly MessageTemplate UploadConversionFailed = Define(
        "upload.conversionFailed",
        "The image could not be converted. {detail}");

    public static readonly MessageTemplate UploadCompressedDamaged = Define(
        "upload.compressedDamaged",
        "The compressed file is damaged: {detail}");

    public static readonly MessageTemplate UploadUnreadableFormat = Define(
        "upload.unreadableFormat",
        "This is a {format} disk image, which DDT does not read. Convert it with qemu-img convert -O raw <file> disk.raw and upload " +
        "disk.raw, or upload the distribution's raw or qcow2 image.");

    public static readonly MessageTemplate UploadNoXz = Define(
        "upload.noXz",
        "This file is compressed with xz, which is not installed on the server. Install xz there and complete the upload again, or " +
        "unpack the file with xz -d and upload the disk image it holds.");

    public static readonly MessageTemplate UploadNoQemuImg = Define(
        "upload.noQemuImg",
        "This is a qcow2 image, and qemu-img is not installed on the server. Install qemu-img there and complete the upload again, or " +
        "convert the file with qemu-img convert -O raw <file> disk.raw and upload disk.raw.");

    public static readonly MessageTemplate UploadQcow2TooShort = Define("upload.qcow2TooShort", "The qcow2 image is too short to be one.");

    public static readonly MessageTemplate UploadQcow2Backing = Define(
        "upload.qcow2Backing",
        "The qcow2 image depends on a backing file. Make a standalone image with qemu-img convert -O qcow2 <file> standalone.qcow2 and " +
        "upload that.");

    public static readonly MessageTemplate UploadQcow2Encrypted = Define(
        "upload.qcow2Encrypted",
        "The qcow2 image is encrypted. Upload an image that is not.");

    public static readonly MessageTemplate UploadQcow2ExternalData = Define(
        "upload.qcow2ExternalData",
        "The qcow2 image keeps its data in an external file. Convert it with qemu-img convert -O raw <file> disk.raw and upload disk.raw.");

    public static readonly MessageTemplate PackageNotZip = Define(
        "package.notZip",
        "The file is not a zip archive, or it is damaged. Upload a zip file.");

    public static readonly MessageTemplate PackageTooManyEntries = Define(
        "package.tooManyEntries",
        "The zip holds {count} entries. A package can hold at most {max}.");

    public static readonly MessageTemplate PackageEntryNameTooLong = Define(
        "package.entryNameTooLong",
        "The entry {entry} has a name longer than {max} characters.");

    public static readonly MessageTemplate PackageEntryControlCharacter = Define(
        "package.entryControlCharacter",
        "The entry {entry} has a control character in its name.");

    public static readonly MessageTemplate PackageEntryAtRoot = Define(
        "package.entryAtRoot",
        "The entry {entry} starts at the root of a drive.");

    public static readonly MessageTemplate PackageEntryTooDeep = Define(
        "package.entryTooDeep",
        "The entry {entry} is more than {max} folders deep.");

    public static readonly MessageTemplate PackageEntryEmptyName = Define(
        "package.entryEmptyName",
        "The entry {entry} has an empty name, or a folder name that points out of the package.");

    public static readonly MessageTemplate PackageEntryForbiddenCharacter = Define(
        "package.entryForbiddenCharacter",
        "The entry {entry} has a character Windows does not allow in names, such as : for a drive or a data stream.");

    public static readonly MessageTemplate PackageEntryTrailingDot = Define(
        "package.entryTrailingDot",
        "The entry {entry} has a name that ends in a dot or a space, which Windows cannot create.");

    public static readonly MessageTemplate PackageEntryDeviceName = Define(
        "package.entryDeviceName",
        "The entry {entry} has a name Windows keeps for a device, such as CON or NUL.");

    public static readonly MessageTemplate PackageEntryEncrypted = Define(
        "package.entryEncrypted",
        "The entry {entry} is encrypted. Upload a zip without a password.");

    public static readonly MessageTemplate PackageEntrySymbolicLink = Define(
        "package.entrySymbolicLink",
        "The entry {entry} is a symbolic link. Put the file itself in the zip.");

    public static readonly MessageTemplate PackageEntryTwice = Define(
        "package.entryTwice",
        "The entry {entry} is in the zip twice, if case is ignored as Windows ignores it.");

    public static readonly MessageTemplate PackageTooLargeUnpacked = Define(
        "package.tooLargeUnpacked",
        "Unpacked, the zip would take more than {max} GB.");

    public static readonly MessageTemplate PackageFileAndFolder = Define(
        "package.fileAndFolder",
        "The zip has a file {entry} and a folder of the same name.");

    public static readonly MessageTemplate PackageNoInf = Define(
        "package.noInf",
        "A driver package needs at least one .inf file. Zip the folder that holds the drivers' .inf files.");

    public static readonly MessageTemplate PackageEntryDamaged = Define(
        "package.entryDamaged",
        "The entry {entry} is damaged. Create the zip again.");

    public static readonly MessageTemplate PackageEntryCompression = Define(
        "package.entryCompression",
        "The entry {entry} is compressed in a way DDT cannot unpack. Create the zip with Deflate.");

    public static readonly MessageTemplate PackageEntrySize = Define(
        "package.entrySize",
        "The entry {entry} unpacks to {actual} bytes, but the zip says {declared}. Create the zip again.");

    public static readonly MessageTemplate PackageEntryChecksum = Define(
        "package.entryChecksum",
        "The entry {entry} does not unpack to the bytes the zip says it holds. Create the zip again.");

    private static MessageTemplate Define(string code, string english)
    {
        MessageTemplate template = new(code, english);
        s_byCode.Add(code, template);
        s_all.Add(template);

        return template;
    }
}

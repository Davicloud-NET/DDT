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
        "sequence.noLocalAdministratorSet",
        "No local administrator is set on the Deployment defaults page, so the answer file cannot add one.");

    public static readonly MessageTemplate SequenceNoDomain = Define(
        "sequence.noDomainSet",
        "No domain is set on the Deployment defaults page, so the machine has no domain to join. Set one there or remove this step.");

    public static readonly MessageTemplate SequenceNoAdministratorWarning = Define(
        "sequence.noAdministratorWarning",
        "The sequence continues in Windows, but no Write answer file step adds the local administrator. Windows setup then stops at " +
        "the account page, and the sequence waits there until someone finishes it.");

    // Value templates, such as PC-{{SerialNumber|alnum|right:12}}. Placeholder is the whole placeholder, braces included.

    public static readonly MessageTemplate ValueTemplateUnknownName = Define(
        "valueTemplate.unknownName",
        "{placeholder} uses {name}, which is not a machine fact or a declared value. Check the spelling.");

    public static readonly MessageTemplate ValueTemplateUnknownFilter = Define(
        "valueTemplate.unknownFilter",
        "{placeholder} uses the filter ''{filter}'', which DDT does not have. The filters are {filters}.");

    public static readonly MessageTemplate ValueTemplateFilterNeedsCount = Define(
        "valueTemplate.filterNeedsCount",
        "In {placeholder}, {filter} needs a number of characters from 1 to {max}, such as {filter}:12.");

    public static readonly MessageTemplate ValueTemplateFilterTakesNoCount = Define(
        "valueTemplate.filterTakesNoCount",
        "In {placeholder}, {filter} takes no number. Remove the colon and what follows it.");

    public static readonly MessageTemplate ValueTemplateNoValue = Define(
        "valueTemplate.noValue",
        "The machine has no value for {placeholder}.");

    // The values of a run, worked out when it starts. Name is the value's name, such as ComputerName.

    public static readonly MessageTemplate ValuesCannotWorkOut = Define(
        "values.cannotWorkOut",
        "{name} cannot be worked out. {problem}");

    public static readonly MessageTemplate ValuesCycle = Define(
        "values.cycle",
        "{name} cannot be worked out, because it is made from itself: {path}.");

    public static readonly MessageTemplate ValuesComputerName = Define(
        "values.computerName",
        "The computer name ''{value}'' cannot be used. {problem}");

    public static readonly MessageTemplate ValuesInputRequired = Define(
        "values.inputRequired",
        "{label} needs an answer before the run can start.");

    public static readonly MessageTemplate ValuesFact = Define(
        "values.fact",
        "{name} is a fact of the machine, which a value cannot set.");

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

    // The ordered rules, the machine roles and the values both set. A rule is saved with its problems, which keep it from
    // matching until they are fixed; a machine role with a problem is refused.

    public static readonly MessageTemplate RuleTooMany = Define(
        "rule.tooMany",
        "There can be at most {max} rules. Delete one before adding another.");

    public static readonly MessageTemplate RuleConditionTooLarge = Define(
        "rule.conditionTooLarge",
        "A rule's condition can have at most {tests} tests, with groups nested at most {depth} deep.");

    public static readonly MessageTemplate RuleTooManyRoles = Define("rule.tooManyRoles", "A rule can give at most {max} machine roles.");

    public static readonly MessageTemplate RuleOrderRepeats = Define("rule.orderRepeats", "The order names a rule more than once.");

    public static readonly MessageTemplate RuleConditionUnreadable = Define(
        "rule.conditionUnreadable",
        "DDT cannot read this rule's condition. Build it again.");

    public static readonly MessageTemplate RuleConditionChooseName = Define("rule.conditionChooseName", "Choose a fact or a value to test.");

    public static readonly MessageTemplate RuleConditionUnknownName = Define(
        "rule.conditionUnknownName",
        "{name} is not a fact of the machine or a value that a rule or a machine role sets. Check the spelling.");

    public static readonly MessageTemplate RuleConditionRunVariable = Define(
        "rule.conditionRunVariable",
        "{name} has a value only while a run goes on, so a rule cannot test it.");

    public static readonly MessageTemplate RuleConditionOperatorType = Define(
        "rule.conditionOperatorType",
        "This comparison does not fit {name}, which holds {type, select, number {a number} yesNo {yes or no} ipv4 {an IPv4 address} " +
        "mac {a MAC address} other {any text}}.");

    public static readonly MessageTemplate RuleConditionNumber = Define("rule.conditionNumber", "Enter a number, such as 8192.");

    public static readonly MessageTemplate RuleConditionYesNo = Define("rule.conditionYesNo", "Enter yes or no.");

    public static readonly MessageTemplate RuleConditionAddress = Define(
        "rule.conditionAddress",
        "Enter an IPv4 address, such as 10.0.0.1.");

    public static readonly MessageTemplate RuleConditionSubnet = Define(
        "rule.conditionSubnet",
        "Write the network as an address and a prefix length, such as 10.0.0.0/24.");

    public static readonly MessageTemplate RuleRoleGone = Define(
        "rule.roleGone",
        "A machine role this rule gives no longer exists. Take it out of the rule.");

    public static readonly MessageTemplate NamedValueTooMany = Define("namedValue.tooMany", "Set at most {max} values here.");

    public static readonly MessageTemplate NamedValueTooLong = Define(
        "namedValue.tooLong",
        "A value's name can have at most {name} characters, and its text at most {text}.");

    public static readonly MessageTemplate NamedValueNameEmpty = Define("namedValue.nameEmpty", "Enter the value's name.");

    public static readonly MessageTemplate NamedValueNameInvalid = Define(
        "namedValue.nameInvalid",
        "''{name}'' cannot name a value. Start with a letter, and use only letters, digits and _, at most {max} characters.");

    public static readonly MessageTemplate NamedValueReserved = Define(
        "namedValue.reserved",
        "Names that start with ddt are DDT's own. Choose another name.");

    public static readonly MessageTemplate NamedValueRepeated = Define("namedValue.repeated", "{name} is set more than once here. Keep one.");

    public static readonly MessageTemplate MachineRoleNameTaken = Define(
        "machineRole.nameTaken",
        "Another machine role is already called {name}. Choose another name.");

    public static readonly MessageTemplate MachineRoleTooMany = Define(
        "machineRole.tooMany",
        "There can be at most {max} machine roles. Delete one before adding another.");

    public static readonly MessageTemplate MachineRoleGivenByRules = Define(
        "machineRole.givenByRules",
        "{count, plural, one {A rule gives this machine role. Take it out of the rule, then delete the role.} " +
        "other {# rules give this machine role. Take it out of them, then delete the role.}}");

    public static readonly MessageTemplate ResolutionRuleNumbered = Define(
        "resolution.ruleNumbered",
        "Rule {number}, {rule}, chooses {sequence}. A rule only chooses: the machine still needs an approval on the web, or someone " +
        "who signs in at it, where the sequence is offered.");

    public static readonly MessageTemplate ResolutionNoRuleChooses = Define(
        "resolution.noRuleChooses",
        "No rule chooses a sequence for this machine, so an operator chooses its sequence.");

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
        "user.roleFromDirectoryGroupMap",
        "The role of {name} comes from its directory groups through the directory's group map, at each sign-in. Change its groups in " +
        "the directory, or the map on the Sign-in page.");

    public static readonly MessageTemplate UserRoleFromSingleSignOnGroups = Define(
        "user.roleFromSingleSignOnGroupMap",
        "The role of {name} comes from its single sign-on groups through the single sign-on group map, at each sign-in. Change its " +
        "groups at the provider, or the map on the Sign-in page.");

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
        "directory.connectionIncomplete",
        "The directory connection is not complete. Enter the directory server and the base DN on the Sign-in page.");

    public static readonly MessageTemplate DirectoryOff = Define(
        "directory.turnedOff",
        "Sign-in through a directory is off. Turn it on and set its connection on the Sign-in page first.");

    public static readonly MessageTemplate DirectoryBindRefused = Define(
        "directory.bindAccountRefused",
        "The directory at {server} refused the bind account {bindDn}. Check the bind account and its password on the Sign-in page.");

    public static readonly MessageTemplate DirectoryUnreachable = Define(
        "directory.unreachable",
        "The directory at {server} could not be reached: {detail}");

    public static readonly MessageTemplate DirectorySearchRefused = Define(
        "directory.baseDnRefused",
        "The directory at {server} refused the search under {baseDn}: {detail} Check the base DN on the Sign-in page.");

    public static readonly MessageTemplate DirectoryNoEntry = Define(
        "directory.noMatchingEntry",
        "No entry under {baseDn} matches {name} with the user filter, so a sign-in with it is refused.");

    public static readonly MessageTemplate DirectoryManyEntries = Define(
        "directory.manyMatchingEntries",
        "More than one entry under {baseDn} matches {name} with the user filter, so a sign-in with it is refused.");

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
        "directory.emptyMapNewAccount",
        "The directory's group map on the Sign-in page is empty, so administrators set roles. A first sign-in makes an account without " +
        "one, which reaches nothing until an administrator gives it a role.");

    public static readonly MessageTemplate DirectoryNoMapNoRole = Define(
        "directory.emptyMapNoRole",
        "The directory's group map on the Sign-in page is empty, so administrators set roles. The account has none yet, so it reaches " +
        "nothing.");

    public static readonly MessageTemplate DirectoryNoMapRole = Define(
        "directory.emptyMapRole",
        "The directory's group map on the Sign-in page is empty, so administrators set roles. The account has {role}.");

    public static readonly MessageTemplate DirectoryNoMappedGroup = Define(
        "directory.inNoMappedGroup",
        "{name} is in none of the groups the directory's group map on the Sign-in page gives a role, so a sign-in is refused.");

    public static readonly MessageTemplate DirectoryRoleFromGroup = Define("directory.roleFromGroup", "{name} gets {role} from {group}.");

    public static readonly MessageTemplate DirectoryRoleFromGroups = Define(
        "directory.roleFromGroups",
        "{name} is in {count} mapped groups and gets the highest role they give, {role} from {group}.");

    public static readonly MessageTemplate DirectoryLockedOut = Define(
        "directory.lockedOut",
        "{reason} The account is locked out for now, so a sign-in waits until the lockout ends.");

    public static readonly MessageTemplate DomainNotConfigured = Define(
        "domain.notSet",
        "No domain is set. Set the domain and the join account on the Deployment defaults page.");

    public static readonly MessageTemplate DomainNoJoinAccount = Define(
        "domain.noJoinAccountSet",
        "The join account is not set. Set it and its password on the Deployment defaults page.");

    public static readonly MessageTemplate DomainCheckError = Define(
        "domain.checkError",
        "{controller} answered the check with an error: {detail}");

    public static readonly MessageTemplate DomainSignedIn = Define(
        "domain.signedIn",
        "Signed in to {controller} as {user} over {connection}.");

    public static readonly MessageTemplate DomainOtherDomain = Define(
        "domain.servesOtherDomain",
        "{controller} serves the domain {namingContext}, not {domain} ({expected}). Correct the domain on the Deployment defaults " +
        "page, or name a domain controller of that domain for the check there.");

    public static readonly MessageTemplate DomainControllerOf = Define(
        "domain.controllerOf",
        "{controller} is a domain controller of {domain}.");

    public static readonly MessageTemplate DomainNoComputersContainer = Define(
        "domain.noComputersContainer",
        "The default Computers container of {domain} was not found, or {user} may not read it.");

    public static readonly MessageTemplate DomainNoOrganizationalUnit = Define(
        "domain.organizationalUnitMissing",
        "{domain} has no organizational unit {organizationalUnit}, or {user} may not read it. Correct the organizational unit of the " +
        "Join the domain step, or the one on the Deployment defaults page when the step names none.");

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
        "domain.joinAccountUnknown",
        "{controller} knows no account {user}. Correct the join account on the Deployment defaults page.");

    public static readonly MessageTemplate DomainWrongPassword = Define(
        "domain.joinPasswordRefused",
        "{controller} did not accept the password of {user}. Correct the join account password on the Deployment defaults page.");

    public static readonly MessageTemplate DomainLogonHours = Define(
        "domain.logonHours",
        "{user} may not sign in at this time of day (logon hours).");

    public static readonly MessageTemplate DomainLogonWorkstations = Define(
        "domain.logonWorkstations",
        "{user} may not sign in from the DDT server (Log On To workstations).");

    public static readonly MessageTemplate DomainPasswordExpired = Define(
        "domain.joinPasswordExpired",
        "The password of {user} has expired. Give it a new one, in the domain and on the Deployment defaults page.");

    public static readonly MessageTemplate DomainAccountDisabled = Define("domain.accountDisabled", "{user} is disabled.");

    public static readonly MessageTemplate DomainAccountExpired = Define("domain.accountExpired", "{user} has expired.");

    public static readonly MessageTemplate DomainMustChangePassword = Define(
        "domain.joinPasswordMustChange",
        "{user} has to change its password before it can sign in. Give it a new one, in the domain and on the Deployment defaults page.");

    public static readonly MessageTemplate DomainLockedOut = Define("domain.lockedOut", "{user} is locked out.");

    public static readonly MessageTemplate DomainSignInRefused = Define(
        "domain.signInRefused",
        "{controller} did not accept the user name or password of {user}.");

    public static readonly MessageTemplate DomainSignInRefusedWithReason = Define(
        "domain.signInRefusedWithReason",
        "{controller} did not accept the user name or password of {user} (reason {reason}).");

    public static readonly MessageTemplate DomainUnreachable = Define(
        "domain.controllerUnreachable",
        "{controller} could not be reached over LDAP. If this server's DNS does not know {domain}, enter a domain controller's name or " +
        "address for the check on the Deployment defaults page. The machines find their domain controller through their own DNS.");

    public static readonly MessageTemplate DomainUnreachableWithDetail = Define(
        "domain.controllerUnreachableWithDetail",
        "{controller} could not be reached over LDAP ({detail}). If this server's DNS does not know {domain}, enter a domain " +
        "controller's name or address for the check on the Deployment defaults page. The machines find their domain controller " +
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

    // Several sentences said as one, such as the problems of a settings section within a sentence that lists them, and
    // a problem after the field it is about. See Sentences.

    public static readonly MessageTemplate CommonSentences = Define("common.sentences", "{first} {rest}");

    public static readonly MessageTemplate SettingsFieldProblem = Define("settings.fieldProblem", "{field}: {problem}");

    // Settings: what a section's fields may not hold, worded for someone looking at the field. The names of settings,
    // such as Domain:Name, are those of configuration.

    public static readonly MessageTemplate SettingsAtLeastOne = Define("settings.atLeastOne", "Must be at least 1.");

    public static readonly MessageTemplate SettingsRoleUnknown = Define("settings.roleUnknown", "''{role}'' is not a DDT role. Use {roles}.");

    public static readonly MessageTemplate SettingsDeploymentTimeZoneUnknown = Define(
        "settings.deployment.timeZoneUnknown",
        "''{value}'' is not a Windows time zone id. Use a name that tzutil /l lists, such as W. Europe Standard Time, or leave it " +
        "empty so that Windows picks the zone of the locale.");

    public static readonly MessageTemplate SettingsDeploymentLocaleUnknown = Define(
        "settings.deployment.localeUnknown",
        "''{value}'' is not a culture name. Use one such as de-DE or en-US, or leave it empty for the image's own language.");

    public static readonly MessageTemplate SettingsDeploymentConsoleLanguageUnknown = Define(
        "settings.deployment.consoleLanguageUnknown",
        "''{value}'' is not a language the console at the machine speaks. Use en or de, or leave it empty for the language of " +
        "Windows PE.");

    public static readonly MessageTemplate SettingsDeploymentAdministratorNameInvalid = Define(
        "settings.deployment.administratorNameInvalid",
        "''{value}'' is not a valid account name. Use 1 to {max} characters and none of \" / \\ [ ] : ; | = , + * ? < >.");

    public static readonly MessageTemplate SettingsDeploymentDomainUserNameRequired = Define(
        "settings.deployment.domainUserNameRequired",
        "Required when Domain:Name is set. Name the account that joins the machines, as DOMAIN\\user or user@domain.example.");

    public static readonly MessageTemplate SettingsDeploymentDomainUserNameForm = Define(
        "settings.deployment.domainUserNameForm",
        "''{value}'' must be written as DOMAIN\\user or user@domain.example.");

    public static readonly MessageTemplate SettingsDeploymentRequiredWithDomain = Define(
        "settings.deployment.requiredWithDomain",
        "Required when Domain:Name is set.");

    public static readonly MessageTemplate SettingsDeploymentAdministratorPasswordRequired = Define(
        "settings.deployment.administratorPasswordRequired",
        "Required when Domain:Name is set. Without a local administrator, a domain machine stops at the account page of its first start.");

    public static readonly MessageTemplate SettingsDeploymentControllerInvalid = Define(
        "settings.deployment.controllerInvalid",
        "''{value}'' is not a host name or an address. Name the domain controller alone, such as dc1.corp.example or 10.0.0.10, " +
        "without a scheme or a port.");

    public static readonly MessageTemplate SettingsMachinesPerAddressRange = Define(
        "settings.machines.perAddressRange",
        "Must be between 1 and MaxWaiting, which is {max}.");

    public static readonly MessageTemplate SettingsMachinesNetworkInvalid = Define(
        "settings.machines.networkInvalid",
        "''{value}'' is not a network. Write each one as an address and a prefix length with no address bits set after the prefix, " +
        "such as 10.20.0.0/16, fd00:20::/64 or 10.20.1.5/32 for one machine.");

    public static readonly MessageTemplate SettingsMachinesNetworkEverything = Define(
        "settings.machines.networkEverything",
        "''{value}'' is every address there is. Name the provisioning networks themselves.");

    public static readonly MessageTemplate SettingsMachinesNetworkHoldsProxy = Define(
        "settings.machines.networkHoldsProxy",
        "The zero touch network {network} contains the proxy {proxy}. A request the proxy forwards without the client's address " +
        "would count as one from that network.");

    public static readonly MessageTemplate SettingsMachinesNetworkOverlapsProxies = Define(
        "settings.machines.networkOverlapsProxies",
        "The zero touch network {network} overlaps the proxy network {proxies}. A request a proxy there forwards without the " +
        "client's address would count as one from that network.");

    public static readonly MessageTemplate SettingsProxiesAddressInvalid = Define(
        "settings.proxies.addressInvalid",
        "''{value}'' is not an IP address.");

    public static readonly MessageTemplate SettingsProxiesNetworkInvalid = Define(
        "settings.proxies.networkInvalid",
        "''{value}'' is not a network such as 10.20.0.0/24 with no address bits set past the prefix length.");

    public static readonly MessageTemplate SettingsProxiesNetworkEverything = Define(
        "settings.proxies.networkEverything",
        "''{value}'' is every address there is, so any client could claim any address. Name the proxies' own network.");

    public static readonly MessageTemplate SettingsLdapHostRequired = Define(
        "settings.ldap.hostRequired",
        "Required while directory sign-in is on. Name the directory server, such as dc1.corp.example.");

    public static readonly MessageTemplate SettingsLdapPortInvalid = Define(
        "settings.ldap.portInvalid",
        "{port} is not a port number. LDAPS uses 636, and StartTLS 389.");

    public static readonly MessageTemplate SettingsLdapTransportInvalid = Define(
        "settings.ldap.transportInvalid",
        "Must be Ldaps, StartTls or UnencryptedDangerous.");

    // The placeholder is {0}, which the catalog cannot say itself: its web tests take a number in braces for an argument
    // that was never named.
    public static readonly MessageTemplate SettingsLdapUserFilterPlaceholder = Define(
        "settings.ldap.userFilterPlaceholder",
        "Must contain {placeholder}, which DDT replaces with the user name, such as (&(objectClass=user)(sAMAccountName={placeholder})).");

    public static readonly MessageTemplate SettingsLdapUserFilterBraces = Define(
        "settings.ldap.userFilterBraces",
        "Is not a valid template: a brace that is not part of {placeholder} has to be written twice, as {open} or {close}.");

    public static readonly MessageTemplate SettingsLdapTimeoutNotPositive = Define(
        "settings.ldap.timeoutNotPositive",
        "Must be longer than zero, such as 00:00:10.");

    public static readonly MessageTemplate SettingsLdapNestedGroupsOff = Define(
        "settings.ldap.nestedGroupsOff",
        "false reads no groups, so every directory user would be refused. Turn it on, or empty GroupRoleMap.");

    public static readonly MessageTemplate SettingsOidcProvisionRoleUnknown = Define(
        "settings.oidc.provisionRoleUnknown",
        "''{role}'' is not a DDT role. Use {viewer} or {operator}.");

    public static readonly MessageTemplate SettingsOidcProvisionAdministrator = Define(
        "settings.oidc.provisionAdministrator",
        "{administrator} would make every identity the provider signs in that DDT has not seen an administrator. Use {viewer} or " +
        "{operator}, or map a group to {administrator} in GroupRoleMap.");

    public static readonly MessageTemplate SettingsOidcGroupsClaimRequired = Define(
        "settings.oidc.groupsClaimRequired",
        "GroupRoleMap needs the claim that carries the groups, such as groups.");

    public static readonly MessageTemplate SettingsOidcScopesWithoutOpenid = Define(
        "settings.oidc.scopesWithoutOpenid",
        "Must contain openid, which is what makes the sign-in OpenID Connect.");

    public static readonly MessageTemplate SettingsOidcAuthorityRequired = Define(
        "settings.oidc.authorityRequired",
        "Required while single sign-on is on: the provider's https address, such as https://login.example.com/realms/ddt.");

    public static readonly MessageTemplate SettingsOidcClientIdRequired = Define(
        "settings.oidc.clientIdRequired",
        "Required while single sign-on is on: the client id the provider shows for DDT.");

    public static readonly MessageTemplate SettingsOidcCannotStart = Define(
        "settings.oidc.cannotStart",
        "Single sign-on cannot start with these values: {error}");

    public static readonly MessageTemplate SettingsOidcSignInScheme = Define(
        "settings.oidc.signInScheme",
        "The handler would sign into ''{scheme}'', which bypasses local account linking entirely.");

    public static readonly MessageTemplate SettingsPxeBootDirectoryEmpty = Define(
        "settings.pxe.bootDirectoryEmpty",
        "Must not be empty. Leave it out for the folder boot in DDT:StorePath.");

    public static readonly MessageTemplate SettingsPxeBootDirectoryIsRoot = Define(
        "settings.pxe.bootDirectoryIsRoot",
        "''{directory}'' is the root of a filesystem, which would serve every file on it. Use a directory of its own, such as {suggested}.");

    public static readonly MessageTemplate SettingsPxeBootDirectoryHoldsStore = Define(
        "settings.pxe.bootDirectoryHoldsStore",
        "''{directory}'' holds DDT:StorePath, {store}, which would serve the database and the key ring. Use a directory of its own, " +
        "such as {suggested}.");

    public static readonly MessageTemplate SettingsPxeBootDirectoryInKeys = Define(
        "settings.pxe.bootDirectoryInKeys",
        "''{directory}'' is in the key ring folder {keys}, which would serve its keys. Use a directory of its own, such as {suggested}.");

    public static readonly MessageTemplate SettingsPxeBootDirectoryHoldsKey = Define(
        "settings.pxe.bootDirectoryHoldsKey",
        "''{directory}'' holds the folder of the TLS certificate or key {file}, which would serve the key. Use a directory of its own, " +
        "such as {suggested}.");

    public static readonly MessageTemplate SettingsPxeNotIpv4Address = Define(
        "settings.pxe.notIpv4Address",
        "''{value}'' is not an IPv4 address.");

    public static readonly MessageTemplate SettingsPxeServerAddressRequired = Define(
        "settings.pxe.serverAddressRequired",
        "Required while EnableTftp is false, because this target uses Tftp. Name the TFTP server that serves it.");

    public static readonly MessageTemplate SettingsPxeHttpBootPortInvalid = Define(
        "settings.pxe.httpBootPortInvalid",
        "{port} is not a port number.");

    public static readonly MessageTemplate SettingsPxeWindowSizeRange = Define("settings.pxe.windowSizeRange", "Must be between 1 and {max}.");

    public static readonly MessageTemplate SettingsPxeArchitectureUnknown = Define(
        "settings.pxe.architectureUnknown",
        "''{value}'' is not a client architecture. Use one of: {architectures}.");

    public static readonly MessageTemplate SettingsPxeMethodInvalid = Define("settings.pxe.methodInvalid", "Must be Tftp or Http.");

    public static readonly MessageTemplate SettingsPxeMethodForArchitecture = Define(
        "settings.pxe.methodForArchitecture",
        "Must be {method} for {architecture} clients.");

    public static readonly MessageTemplate SettingsPxeBootFileRequired = Define("settings.pxe.bootFileRequired", "Must be set.");

    public static readonly MessageTemplate SettingsPxeAsciiMaxLength = Define(
        "settings.pxe.asciiMaxLength",
        "Must be ASCII and at most {max} characters.");

    public static readonly MessageTemplate SettingsPxeBootFileUrl = Define("settings.pxe.bootFileUrl", "Must be an absolute http or https URL.");

    public static readonly MessageTemplate SettingsLoggingLevelUnknown = Define(
        "settings.logging.levelUnknown",
        "''{value}'' is not a log level. Use Trace, Debug, Information, Warning, Error, Critical or None.");

    public static readonly MessageTemplate SettingsCertificateNameInvalid = Define(
        "settings.certificate.nameInvalid",
        "''{name}'' is not a host name or an address. Write each name alone, such as ddt.corp.example or 10.0.0.5.");

    // Settings: stored values and secrets this server cannot read, and a save that needs more than the values.

    public static readonly MessageTemplate SettingsValuesCannotBeChecked = Define(
        "settings.valuesCannotBeChecked",
        "The values cannot be checked: {error}");

    public static readonly MessageTemplate SettingsConfigurationUnreadable = Define(
        "settings.configurationUnreadable",
        "{section} cannot be read from configuration: {error}");

    public static readonly MessageTemplate SettingsStoredSecretUnreadable = Define(
        "settings.storedSecretUnreadable",
        "The stored value no longer decrypts with this server's key ring. Enter it again.");

    public static readonly MessageTemplate SettingsStoredValueUnreadable = Define(
        "settings.storedValueUnreadable",
        "The stored value cannot be read, so the default applies: {error}");

    public static readonly MessageTemplate SettingsSecretCannotBeKept = Define(
        "settings.secretCannotBeKept",
        "No longer decrypts with this server's key ring, so it cannot be kept. Enter it again or clear it.");

    public static readonly MessageTemplate SettingsSecretForNewServer = Define(
        "settings.secretForNewServer",
        "Enter it again for the new server: a stored secret goes only to the server it was entered for.");

    public static readonly MessageTemplate SettingsLdapTestOwnSignIn = Define(
        "settings.ldap.testOwnSignIn",
        "You sign in through the directory, and these values decide whether you still can. Test your own sign-in with them first; " +
        "the save is accepted while a test that kept you an administrator is less than 5 minutes old.");

    // Settings: warnings. A save asks to confirm those with a confirmation code; the others only inform.

    public static readonly MessageTemplate SettingsNoLocalAdministrator = Define(
        "settings.noLocalAdministrator",
        "No local administrator account is enabled. Should the directory or the provider stop granting the Administrator role, only " +
        "the console command settings create-admin could let anyone in again.");

    public static readonly MessageTemplate SettingsMachinesNetworkWide = Define(
        "settings.machines.networkWide",
        "{network} is wider than a /{prefix}. Every address in it counts as a zero touch address.");

    public static readonly MessageTemplate SettingsProxiesNetworkWide = Define(
        "settings.proxies.networkWide",
        "{network} is wider than a /{prefix}. Every address in it counts as a trusted proxy address.");

    public static readonly MessageTemplate SettingsLdapUnencrypted = Define(
        "settings.ldap.unencrypted",
        "The bind password and every password typed at sign-in cross the network in clear text.");

    public static readonly MessageTemplate SettingsLdapRekey = Define(
        "settings.ldap.rekey",
        "Every directory account is keyed on {current}. With {next} each one is taken for a new person at its next sign-in, and its " +
        "roles and history stay with the old account.");

    public static readonly MessageTemplate SettingsLdapNoAdministrator = Define(
        "settings.ldap.noAdministrator",
        "No group maps to Administrator, so every directory account that is an administrator loses the role at its next sign-in.");

    public static readonly MessageTemplate SettingsOidcOperatorRole = Define(
        "settings.oidc.operatorRole",
        "Every identity the provider signs in that DDT has not seen becomes an operator, and operators can read the deployment " +
        "passwords by deploying a machine they control.");

    public static readonly MessageTemplate SettingsPxeBootUrl = Define(
        "settings.pxe.bootUrl",
        "{url} is not where DDT serves boot files, which is http://<server>:{port}/boot/. Keep it only if another server serves this file.");

    public static readonly MessageTemplate SettingsPxeInterfaceNotFound = Define(
        "settings.pxe.interfaceNotFound",
        "''{name}'' names no interface on {host}, which serves nothing for it.");

    // Settings: saving a section, and proving who you are again.

    public static readonly MessageTemplate SettingsSendValues = Define("settings.sendValues", "Send the values of the section.");

    public static readonly MessageTemplate SettingsNoSuchSecret = Define("settings.noSuchSecret", "{section} has no secret called {name}.");

    public static readonly MessageTemplate SettingsSavedSince = Define(
        "settings.savedSince",
        "Someone saved {section} since you loaded it. Load it again.");

    public static readonly MessageTemplate SettingsEnterPasswordAgain = Define(
        "settings.enterPasswordAgain",
        "Enter your password again to change {fields}.");

    public static readonly MessageTemplate SettingsKeyRingUnreadable = Define(
        "settings.keyRingUnreadable",
        "This server cannot read the key ring the stored settings secrets were encrypted with, so it saves no settings. Every DDT " +
        "process on one database has to share the key ring in DDT:StorePath/keys.");

    public static readonly MessageTemplate SettingsReauthenticateNoPassword = Define(
        "settings.reauthenticate.noPassword",
        "This account signs in without a password DDT can check, so it cannot change these settings. Use an account with a local or " +
        "directory password.");

    public static readonly MessageTemplate SettingsReauthenticateLockedOut = Define(
        "settings.reauthenticate.lockedOut",
        "The account is locked out. Try again later.");

    public static readonly MessageTemplate SettingsReauthenticateCodeNeeded = Define(
        "settings.reauthenticate.codeNeeded",
        "Enter the code of your authenticator as well.");

    public static readonly MessageTemplate SettingsReauthenticateNotRight = Define(
        "settings.reauthenticate.notRight",
        "The password or the code is not right.");

    // Settings: testing the directory and the single sign-on provider with values not yet saved.

    public static readonly MessageTemplate SettingsLdapTestSendValues = Define("settings.ldapTest.sendValues", "Send the values to test.");

    public static readonly MessageTemplate SettingsLdapTestNotDirectoryAccount = Define(
        "settings.ldapTest.notDirectoryAccount",
        "{name} is not a directory account, so its password is not sent to the directory.");

    public static readonly MessageTemplate SettingsLdapTestAccountDisabled = Define(
        "settings.ldapTest.accountDisabled",
        "{name} is disabled, so a sign-in is refused before the directory is asked.");

    public static readonly MessageTemplate SettingsLdapTestLockedOut = Define(
        "settings.ldapTest.lockedOut",
        "{name} is locked out, so a sign-in is refused before the directory is asked.");

    public static readonly MessageTemplate SettingsLdapTestBindFailed = Define(
        "settings.ldapTest.bindFailed",
        "The bind as {account} to {server} failed: {error}");

    public static readonly MessageTemplate SettingsLdapTestBound = Define(
        "settings.ldapTest.bound",
        "The bind as {account} to {server} succeeded.");

    public static readonly MessageTemplate SettingsLdapTestManyEntries = Define(
        "settings.ldapTest.manyEntries",
        "More than one entry under {baseDn} matches {name} with the user filter, so a sign-in is refused.");

    public static readonly MessageTemplate SettingsLdapTestNoEntry = Define(
        "settings.ldapTest.noEntry",
        "No entry under {baseDn} matches {name} with the user filter.");

    public static readonly MessageTemplate SettingsLdapTestPasswordRefused = Define(
        "settings.ldapTest.passwordRefused",
        "{entry} was found, but the directory refused the password.");

    public static readonly MessageTemplate SettingsLdapTestNoImmutableId = Define(
        "settings.ldapTest.noImmutableId",
        "{entry} has no {attribute}, so a sign-in is refused.");

    public static readonly MessageTemplate SettingsLdapTestFound = Define(
        "settings.ldapTest.found",
        "{entry} was found, in {count, plural, one {# group} other {# groups}}.");

    public static readonly MessageTemplate SettingsLdapTestSearchFailed = Define(
        "settings.ldapTest.searchFailed",
        "The search for {name} under {baseDn} failed: {error}");

    // Result is the directory's answer, a message of its own.
    public static readonly MessageTemplate SettingsLdapTestNoRole = Define(
        "settings.ldapTest.noRole",
        "{result} The group map gives no role, so a sign-in is refused.");

    public static readonly MessageTemplate SettingsLdapTestRole = Define(
        "settings.ldapTest.role",
        "{result} The group map makes the account {role}.");

    public static readonly MessageTemplate SettingsOidcTestAuthorityInvalid = Define(
        "settings.oidcTest.authorityInvalid",
        "Enter the provider's https address, such as https://login.example.com/realms/ddt.");

    public static readonly MessageTemplate SettingsOidcTestAnswered = Define(
        "settings.oidcTest.answered",
        "{url} answered {status} {reason}.");

    public static readonly MessageTemplate SettingsOidcTestNotDiscovery = Define(
        "settings.oidcTest.notDiscovery",
        "{url} is not the discovery document of an OpenID Connect provider.");

    public static readonly MessageTemplate SettingsOidcTestReached = Define(
        "settings.oidcTest.reached",
        "The provider answered as {issuer}. Register {redirectUri} as the redirect URI of DDT's client there.");

    public static readonly MessageTemplate SettingsOidcTestOtherIssuer = Define(
        "settings.oidcTest.otherIssuer",
        "The provider names itself {issuer}, not {authority}. Enter {issuer} as the authority.");

    public static readonly MessageTemplate SettingsOidcTestUnreadable = Define(
        "settings.oidcTest.unreadable",
        "{url} could not be read: {error}");

    // Settings: the server certificate, and the agent that netbooting machines run.

    public static readonly MessageTemplate SettingsCertificateNotManageable = Define(
        "settings.certificate.notManageable",
        "The page manages the certificate only when Kestrel:Certificates:Default:Path and KeyPath both name PEM files and no Password " +
        "is set. A PFX, a key under a password, or TLS at a proxy is managed by hand.");

    public static readonly MessageTemplate SettingsCertificateGenerateOff = Define(
        "settings.certificate.generateOff",
        "DDT:Https:GenerateSelfSignedCertificate is false, so DDT issues no certificate. Upload one instead.");

    public static readonly MessageTemplate SettingsCertificateAddHostFirst = Define(
        "settings.certificate.addHostFirst",
        "Add {host}, the name this page is reached by, to the server names first.");

    public static readonly MessageTemplate SettingsCertificateNewRootUpload = Define(
        "settings.certificate.newRootUpload",
        "This certificate does not come from DDT's root, which every boot image pins: build every boot image again with its root, and " +
        "trust that root in the browsers that manage DDT.");

    public static readonly MessageTemplate SettingsCertificateNewRootGenerate = Define(
        "settings.certificate.newRootGenerate",
        "DDT has no root yet, so Generate makes one: build every boot image again with it, and trust it in the browsers that manage DDT.");

    public static readonly MessageTemplate SettingsCertificateNothingToConfirm = Define(
        "settings.certificate.nothingToConfirm",
        "No certificate waits for a confirmation.");

    public static readonly MessageTemplate SettingsCertificateNotServedNew = Define(
        "settings.certificate.notServedNew",
        "This connection was served the certificate before the new one, so it proves nothing about the new one. Load the page again, " +
        "which connects anew, and confirm from there.");

    public static readonly MessageTemplate SettingsCertificateSendPair = Define(
        "settings.certificate.sendPair",
        "Send the certificate and its key, or a PFX.");

    public static readonly MessageTemplate SettingsCertificateNotBase64 = Define("settings.certificate.notBase64", "Is not base64.");

    public static readonly MessageTemplate SettingsCertificatePfxWithoutKey = Define(
        "settings.certificate.pfxWithoutKey",
        "Holds no certificate with its private key.");

    public static readonly MessageTemplate SettingsCertificateKeyAlgorithm = Define(
        "settings.certificate.keyAlgorithm",
        "Its key is neither RSA nor ECDSA.");

    public static readonly MessageTemplate SettingsCertificatePfxPassword = Define(
        "settings.certificate.pfxPassword",
        "Does not open with this password: {error}");

    public static readonly MessageTemplate SettingsCertificateSendPem = Define(
        "settings.certificate.sendPem",
        "Send the certificate with its intermediates and its key as PEM, or a PFX.");

    public static readonly MessageTemplate SettingsCertificateKeyMismatch = Define(
        "settings.certificate.keyMismatch",
        "The certificate does not load with this key: {error}");

    public static readonly MessageTemplate SettingsCertificateNotValidNow = Define(
        "settings.certificate.notValidNow",
        "It is valid from {from} to {until}, not now.");

    public static readonly MessageTemplate SettingsCertificateMissingNames = Define(
        "settings.certificate.missingNames",
        "It does not name {names}, which the server is reached by, so browsers and agents would refuse it.");

    public static readonly MessageTemplate SettingsAgentConfigured = Define(
        "settings.agent.configured",
        "DDT:Agent:BinaryPath names the agent in configuration, so it cannot be uploaded here. Remove the key to upload it on this page.");

    public static readonly MessageTemplate SettingsAgentNotExecutable = Define(
        "settings.agent.notExecutable",
        "That is not a Windows executable. Upload ddt-agent.exe as Publish-Agent.ps1 builds it.");

    public static readonly MessageTemplate SettingsAgentTooLarge = Define("settings.agent.tooLarge", "The agent may be at most {max} MB.");

    public static readonly MessageTemplate SettingsConsoleConfigured = Define(
        "settings.console.configured",
        "DDT:Agent:ConsolePath names the console in configuration, so it cannot be uploaded here. Remove the key to upload it on this page.");

    public static readonly MessageTemplate SettingsConsoleNotAPackage = Define(
        "settings.console.notAPackage",
        "That is not the console. Upload a zip of the folder Publish-Console.ps1 writes, with ddt-console.exe, libSkiaSharp.dll and libHarfBuzzSharp.dll and nothing else.");

    public static readonly MessageTemplate SettingsConsoleTooLarge = Define(
        "settings.console.tooLarge",
        "The console may be at most {max} MB, zipped and unpacked.");

    public static readonly MessageTemplate SettingsConsoleLogoNotPng = Define(
        "settings.consoleLogo.notPng",
        "That is not a PNG image. Upload the logo as a PNG file.");

    public static readonly MessageTemplate SettingsConsoleLogoDimensions = Define(
        "settings.consoleLogo.dimensions",
        "The logo is {width} by {height} pixels. It may be at most {max} pixels wide and high.");

    public static readonly MessageTemplate SettingsConsoleLogoTooLarge = Define("settings.consoleLogo.tooLarge", "The logo may be at most {max} KB.");

    // Settings: how a host applied a section that rebuilds a subsystem. An exception's own text stays English, as a value.

    public static readonly MessageTemplate SettingsApplyProxiesClosed = Define(
        "settings.apply.proxiesClosed",
        "No proxy is trusted on this host while the section has problems: {problems}");

    public static readonly MessageTemplate SettingsApplyOidcClosed = Define(
        "settings.apply.oidcClosed",
        "Single sign-on is off on this host while the section has problems: {problems}");

    public static readonly MessageTemplate SettingsApplyOidcFailed = Define(
        "settings.apply.oidcFailed",
        "Single sign-on is off on this host: {error}");

    public static readonly MessageTemplate SettingsApplyPxeClosed = Define(
        "settings.apply.pxeClosed",
        "The pxe settings have problems, so nothing is served until they are fixed: {problems}");

    // Error is the name of the socket error, which also chooses the advice.
    public static readonly MessageTemplate SettingsApplyPxeBindFailed = Define(
        "settings.apply.pxeBindFailed",
        "DDT could not bind UDP {port} for {protocol, select, proxyDhcp {ProxyDHCP} bootServer {PXE boot server} tftp {TFTP} " +
        "tftpSinglePort {TFTP (single port)} other {{protocol}}} ({error}). {error, select, AccessDenied {The process may not bind a " +
        "privileged port. Grant NET_BIND_SERVICE, as build/compose.yaml does.} AddressAlreadyInUse {Another DHCP, PXE or TFTP " +
        "service already holds this port on this host. Stop it, or run DDT without the pxe role here.} other {Check that no other " +
        "service holds the port.}}");

    // The sentences one after another, as one message, which the web says in the person's language as a whole: the
    // first, then the rest as a message of its own.
    public static ServerMessage Sentences(IReadOnlyList<ServerMessage> sentences)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        ArgumentOutOfRangeException.ThrowIfZero(sentences.Count);

        ServerMessage said = sentences[^1];

        for (int index = sentences.Count - 2; index >= 0; index--)
        {
            said = CommonSentences.With("first", sentences[index], "rest", said);
        }

        return said;
    }

    private static MessageTemplate Define(string code, string english)
    {
        MessageTemplate template = new(code, english);
        s_byCode.Add(code, template);
        s_all.Add(template);

        return template;
    }
}

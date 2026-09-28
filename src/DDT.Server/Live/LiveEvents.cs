// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Live;

public static class LiveEvents
{
    public const string MachineChanged = "machineChanged";

    // Carries a MachinesRemovedEvent, so clients drop the machines from their lists without loading them again. The
    // machines' runs are gone with them, so the run history drops those too.
    public const string MachinesRemoved = "machinesRemoved";

    // Carries a RunHistoryItem whenever a run or the machine it names changes, so the run history upserts it by run id.
    public const string RunChanged = "runChanged";

    // Carries the ImageSummary of an image that was added or changed, which clients upsert by id.
    public const string ImageChanged = "imageChanged";

    // Carries an ImagesRemovedEvent, so clients drop the images without loading the library again.
    public const string ImagesRemoved = "imagesRemoved";

    // Carries a SequenceChangedEvent, so an editor can tell another administrator's save from its own.
    public const string SequenceChanged = "sequenceChanged";

    // Carries the PackageSummary of a package that was added or changed, which clients upsert by id.
    public const string PackageChanged = "packageChanged";

    // Carries a PackagesRemovedEvent, so clients drop the packages without loading the library again.
    public const string PackagesRemoved = "packagesRemoved";

    // Carries a BootImageView whenever the drivers flagged for the boot image or the build in the boot directory change.
    public const string BootImageChanged = "bootImageChanged";

    // Carries every rule, AssignmentRuleView in the order the Rules page lists them: rules are few, and one change can
    // move a rule among the others or rename the sequence several of them choose.
    public const string RulesChanged = "rulesChanged";

    // Carries a RunStepChangedEvent, only to the connections that watch the machine.
    public const string RunStepChanged = "runStepChanged";

    // Carries a MachineLogAppendedEvent, only to the connections that watch the machine.
    public const string MachineLogAppended = "machineLogAppended";

    // Carries a UserView, only to administrators: an account was created or changed, or signed in.
    public const string UserChanged = "userChanged";

    // Carries a UsersRemovedEvent, only to administrators.
    public const string UsersRemoved = "usersRemoved";

    // Carries the AuditEntry rows one save added, oldest first, only to the connections of administrators.
    public const string AuditAppended = "auditAppended";

    // Carries an ApiTokenView whenever a token is created, used or revoked, to administrators and to its owner. A
    // connection in both groups may receive it twice, which an upsert by id does not notice.
    public const string TokenChanged = "tokenChanged";

    // Carries the SettingsSectionView of the section, as GET /api/settings/{section} answers it, whenever it was saved or
    // a host applied it: to administrators, and for the deployment and machines sections also to operators.
    public const string SettingsChanged = "settingsChanged";

    // Carries every pxe host's candidate interfaces, as GET /api/settings/pxe/interfaces answers them, whenever a host
    // applied the pxe section and reported what it found: to administrators.
    public const string PxeInterfacesChanged = "pxeInterfacesChanged";

    // Carries the AgentBinaryView, as GET /api/settings/agent answers it, when an agent was uploaded: to administrators.
    public const string AgentChanged = "agentChanged";

    // The same for the console, as GET /api/settings/agent/console answers it, when a console was uploaded: to
    // administrators.
    public const string ConsoleChanged = "consoleChanged";

    // Carries the ConsoleLogoView, as GET /api/settings/console-logo answers it, when the console's logo was uploaded or
    // removed: to administrators and operators, who read the Deployment defaults page it is on.
    public const string ConsoleLogoChanged = "consoleLogoChanged";

    // Carries the CertificateView, without servedHere, when a certificate was installed, confirmed or rolled back: to
    // administrators.
    public const string CertificateChanged = "certificateChanged";

    // Carries the AccountView of an account that steps use, as GET /api/accounts/{id} answers it, whenever it was created or
    // changed or a sequence started or stopped naming it. It holds no password, only whether one is set.
    public const string AccountChanged = "accountChanged";

    // Carries an AccountsRemovedEvent, so clients drop the accounts without loading the list again.
    public const string AccountsRemoved = "accountsRemoved";
}

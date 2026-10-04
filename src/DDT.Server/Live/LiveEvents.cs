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

    // Carries a RunHistoryItem whenever a run or its machine changes. The run history upserts it by run ID.
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

    // Carries a BootImageJobOutput: the lines a build or an ADK install wrote since the last push.
    public const string BootImageJobOutput = "bootImageJobOutput";

    // Carries every rule, top first, the same as GET /api/rules returns. Rules are few, and one change can move
    // several, rename a sequence they choose, or change what they test or how many machines they match.
    public const string RulesChanged = "rulesChanged";

    // Carries every machine role as a MachineRoleView, by name, the same as GET /api/machine-roles returns. A rule
    // change changes how many rules give each role.
    public const string RolesChanged = "rolesChanged";

    // Carries a RunStepChangedEvent, only to the connections that watch the machine.
    public const string RunStepChanged = "runStepChanged";

    // Carries a RunVariablesChangedEvent, only to the connections that watch the machine.
    public const string RunVariablesChanged = "runVariablesChanged";

    // Carries a MachineLogAppendedEvent, only to the connections that watch the machine.
    public const string MachineLogAppended = "machineLogAppended";

    // Carries a UserView, only to administrators, when an account was created, changed or signed in.
    public const string UserChanged = "userChanged";

    // Carries a UsersRemovedEvent, only to administrators.
    public const string UsersRemoved = "usersRemoved";

    // Carries the AuditEntry rows one save added, oldest first, only to the connections of administrators.
    public const string AuditAppended = "auditAppended";

    // Carries an ApiTokenView whenever a token is created, used or revoked, to administrators and to its owner. A
    // connection in both groups may get it twice. That's harmless, because clients upsert it by ID.
    public const string TokenChanged = "tokenChanged";

    // Carries the section's SettingsSectionView, the same as GET /api/settings/{section} returns, when it was saved or
    // a host applied it. Goes to administrators, and for the deployment and machines sections to operators too.
    public const string SettingsChanged = "settingsChanged";

    // Carries every PXE host's candidate interfaces, the same as GET /api/settings/pxe/interfaces returns, when a host
    // applied the pxe section and reported what it found. Goes to administrators.
    public const string PxeInterfacesChanged = "pxeInterfacesChanged";

    // Carries an ImportStatus to administrators: how far an import from the server's disks is, and what it added.
    public const string ImportChanged = "importChanged";

    // Carries the AgentBinaryView, the same as GET /api/settings/agent returns, when an agent was uploaded. Goes to
    // administrators.
    public const string AgentChanged = "agentChanged";

    // The same for the console, as GET /api/settings/agent/console returns it, when a console was uploaded. Goes to
    // administrators.
    public const string ConsoleChanged = "consoleChanged";

    // Carries the ConsoleLogoView, the same as GET /api/settings/console-logo returns, when the console logo was
    // uploaded or removed. Goes to administrators and operators, because both read the Deployment defaults page.
    public const string ConsoleLogoChanged = "consoleLogoChanged";

    // Carries the CertificateView, without servedHere, when a certificate was installed, confirmed or rolled back. Goes
    // to administrators.
    public const string CertificateChanged = "certificateChanged";

    // Carries an account's AccountView, the same as GET /api/accounts/{id} returns, when it was created or changed, or
    // a sequence started or stopped using it. It holds no password, only whether one is set.
    public const string AccountChanged = "accountChanged";

    // Carries an AccountsRemovedEvent, so clients drop the accounts without loading the list again.
    public const string AccountsRemoved = "accountsRemoved";
}

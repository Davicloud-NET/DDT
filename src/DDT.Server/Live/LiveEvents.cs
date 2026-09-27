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

    // Carries the AuditEntry rows one save added, oldest first, only to the connections of administrators.
    public const string AuditAppended = "auditAppended";

    // Carries an ApiTokenView whenever a token is created, used or revoked, to administrators and to its owner. A
    // connection in both groups may receive it twice, which an upsert by id does not notice.
    public const string TokenChanged = "tokenChanged";
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Live;

public static class LiveEvents
{
    public const string MachineChanged = "machineChanged";

    // Carries a MachinesRemovedEvent, so clients drop the machines from their lists without loading them again.
    public const string MachinesRemoved = "machinesRemoved";

    // Carries nothing either: clients load the image list again.
    public const string ImagesChanged = "imagesChanged";

    // Carries a SequenceChangedEvent, so an editor can tell another administrator's save from its own.
    public const string SequenceChanged = "sequenceChanged";

    // Carries nothing: clients load the package list again.
    public const string PackagesChanged = "packagesChanged";

    // Carries nothing: clients load the rules again.
    public const string RulesChanged = "rulesChanged";

    // Carries a RunStepChangedEvent, only to the connections that watch the machine.
    public const string RunStepChanged = "runStepChanged";

    // Carries a MachineLogAppendedEvent, only to the connections that watch the machine.
    public const string MachineLogAppended = "machineLogAppended";
}

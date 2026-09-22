// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

public static class AuditActions
{
    public const string MachineRegistered = "machine.registered";
    public const string MachineReregistered = "machine.reregistered";
    public const string MachineSignedIn = "machine.signed-in";
    public const string MachineApproved = "machine.approved";
    public const string MachineRejected = "machine.rejected";
    public const string MachineRemoved = "machine.removed";
    public const string ImageUploaded = "image.uploaded";
    public const string ImageDeleted = "image.deleted";
    public const string DeploymentAssigned = "deployment.assigned";
    public const string DeploymentCancelled = "deployment.cancelled";
    public const string DeploymentStarted = "deployment.started";
    public const string DeploymentDone = "deployment.done";
    public const string DeploymentFailed = "deployment.failed";
    public const string CertificateAnchorAcknowledged = "certificate.anchor-acknowledged";
    public const string SequenceCreated = "sequence.created";
    public const string SequenceChanged = "sequence.changed";
    public const string SequenceDeleted = "sequence.deleted";
}

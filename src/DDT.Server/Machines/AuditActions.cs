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
    public const string DeploymentSecretRead = "deployment.secret-read";
    public const string DeploymentResumed = "deployment.resumed";
    public const string DeploymentRunTokenRefused = "deployment.run-token-refused";
    public const string CertificateAnchorAcknowledged = "certificate.anchor-acknowledged";
    public const string DomainJoinChecked = "domain.join-checked";
    public const string SequenceCreated = "sequence.created";
    public const string SequenceChanged = "sequence.changed";
    public const string SequenceDeleted = "sequence.deleted";
    public const string PackageUploaded = "package.uploaded";
    public const string PackageChanged = "package.changed";
    public const string PackageDeleted = "package.deleted";
    public const string RuleCreated = "rule.created";
    public const string RuleChanged = "rule.changed";
    public const string RuleDeleted = "rule.deleted";
    public const string UserCreated = "user.created";
    public const string UserChanged = "user.changed";
    public const string UserDisabled = "user.disabled";
    public const string UserEnabled = "user.enabled";
    public const string UserDeleted = "user.deleted";
    public const string UserPasswordReset = "user.password-reset";
    public const string UserTwoFactorReset = "user.two-factor-reset";
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;
using DDT.Contracts.About;
using DDT.Contracts.Audit;
using DDT.Contracts.BootImage;
using DDT.Contracts.Authentication;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;
using DDT.Contracts.Packages;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Contracts.Server;
using DDT.Contracts.Tokens;

namespace DDT.Contracts;

// Out of order metadata: PostgreSQL jsonb and browsers may put a step's "kind" after its other properties.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(CurrentUser))]
[JsonSerializable(typeof(ChangePasswordRequest))]
[JsonSerializable(typeof(TwoFactorEnrollment))]
[JsonSerializable(typeof(TwoFactorVerifyRequest))]
[JsonSerializable(typeof(RecoveryCodes))]
[JsonSerializable(typeof(MachineSummary))]
[JsonSerializable(typeof(IReadOnlyList<MachineSummary>))]
[JsonSerializable(typeof(MachineLogPage))]
[JsonSerializable(typeof(MachineLogAppendedEvent))]
[JsonSerializable(typeof(MachinesRemovedEvent))]
[JsonSerializable(typeof(ImageSummary))]
[JsonSerializable(typeof(ImagesRemovedEvent))]
[JsonSerializable(typeof(IReadOnlyList<ImageSummary>))]
[JsonSerializable(typeof(CreateImageUploadRequest))]
[JsonSerializable(typeof(ImageUploadSession))]
[JsonSerializable(typeof(IReadOnlyList<ImageUploadSession>))]
[JsonSerializable(typeof(AssignSequenceRequest))]
[JsonSerializable(typeof(ApproveMachineRequest))]
[JsonSerializable(typeof(DeploymentView))]
[JsonSerializable(typeof(IReadOnlyList<DeploymentSummary>))]
[JsonSerializable(typeof(RunHistoryPage))]
[JsonSerializable(typeof(RunHistoryItem))]
[JsonSerializable(typeof(RunStepChangedEvent))]
[JsonSerializable(typeof(DeploymentOptionsView))]
[JsonSerializable(typeof(DomainJoinCheckRequest))]
[JsonSerializable(typeof(DomainJoinCheckView))]
[JsonSerializable(typeof(AboutInfo))]
[JsonSerializable(typeof(ServerCertificateView))]
[JsonSerializable(typeof(SequenceDefinition))]
[JsonSerializable(typeof(IReadOnlyList<SequenceProblem>))]
[JsonSerializable(typeof(IReadOnlyList<SequenceSummary>))]
[JsonSerializable(typeof(SequenceView))]
[JsonSerializable(typeof(CreateSequenceRequest))]
[JsonSerializable(typeof(SaveSequenceRequest))]
[JsonSerializable(typeof(SequenceValidation))]
[JsonSerializable(typeof(IReadOnlyList<SequenceTemplate>))]
[JsonSerializable(typeof(SequenceChangedEvent))]
[JsonSerializable(typeof(PackageSummary))]
[JsonSerializable(typeof(PackagesRemovedEvent))]
[JsonSerializable(typeof(IReadOnlyList<PackageSummary>))]
[JsonSerializable(typeof(UpdatePackageRequest))]
[JsonSerializable(typeof(AssignmentRuleView))]
[JsonSerializable(typeof(IReadOnlyList<AssignmentRuleView>))]
[JsonSerializable(typeof(AssignmentRuleView[]))]
[JsonSerializable(typeof(SaveAssignmentRuleRequest))]
[JsonSerializable(typeof(MachineSequenceResolution))]
[JsonSerializable(typeof(IReadOnlyList<HardwareModelCount>))]
[JsonSerializable(typeof(AuditPage))]
[JsonSerializable(typeof(AuditEntry[]))]
[JsonSerializable(typeof(ApiTokenView))]
[JsonSerializable(typeof(IReadOnlyList<ApiTokenView>))]
[JsonSerializable(typeof(CreateApiTokenRequest))]
[JsonSerializable(typeof(CreatedApiToken))]
[JsonSerializable(typeof(BootImageView))]
public sealed partial class DdtJsonContext : JsonSerializerContext;

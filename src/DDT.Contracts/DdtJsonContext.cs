// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using System.Text.Json.Serialization;
using DDT.Contracts.About;
using DDT.Contracts.Accounts;
using DDT.Contracts.Audit;
using DDT.Contracts.BootImage;
using DDT.Contracts.Import;
using DDT.Contracts.Netboot;
using DDT.Contracts.Authentication;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;
using DDT.Contracts.Messages;
using DDT.Contracts.Packages;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Contracts.Server;
using DDT.Contracts.Settings;
using DDT.Contracts.Tokens;
using DDT.Contracts.Users;
using DDT.Contracts.Values;

namespace DDT.Contracts;

// Out of order metadata is allowed because PostgreSQL jsonb and browsers may put a step's "kind" after its other
// properties. A message's values are objects, so the problem details need every type a value can have. They also
// need JsonElement for values read back from JSON.
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
[JsonSerializable(typeof(IReadOnlyList<ExternalProvider>))]
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
[JsonSerializable(typeof(RunVariablesChangedEvent))]
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
[JsonSerializable(typeof(MachineSequenceResolution))]
[JsonSerializable(typeof(IReadOnlyList<HardwareModelCount>))]
[JsonSerializable(typeof(AuditPage))]
[JsonSerializable(typeof(AuditEntry[]))]
[JsonSerializable(typeof(ApiTokenView))]
[JsonSerializable(typeof(IReadOnlyList<ApiTokenView>))]
[JsonSerializable(typeof(CreateApiTokenRequest))]
[JsonSerializable(typeof(CreatedApiToken))]
[JsonSerializable(typeof(BootImageView))]
[JsonSerializable(typeof(BootImageJobLog))]
[JsonSerializable(typeof(BootImageJobOutput))]
[JsonSerializable(typeof(BuildBootImageRequest))]
[JsonSerializable(typeof(UseBootImageBuildRequest))]
[JsonSerializable(typeof(SetupChecklist))]
[JsonSerializable(typeof(NetbootNeighbours))]
[JsonSerializable(typeof(IReadOnlyList<DhcpScope>))]
[JsonSerializable(typeof(SetDhcpOptionsRequest))]
[JsonSerializable(typeof(ImportSources))]
[JsonSerializable(typeof(ImportStatus))]
[JsonSerializable(typeof(ImportFilesRequest))]
[JsonSerializable(typeof(InspectMdtShareRequest))]
[JsonSerializable(typeof(ImportMdtShareRequest))]
[JsonSerializable(typeof(MdtShareView))]
[JsonSerializable(typeof(UserView))]
[JsonSerializable(typeof(IReadOnlyList<UserView>))]
[JsonSerializable(typeof(CreateUserRequest))]
[JsonSerializable(typeof(UpdateUserRequest))]
[JsonSerializable(typeof(CreatedUser))]
[JsonSerializable(typeof(OneTimePassword))]
[JsonSerializable(typeof(UsersRemovedEvent))]
[JsonSerializable(typeof(DirectoryView))]
[JsonSerializable(typeof(IReadOnlyList<DirectoryGroup>))]
[JsonSerializable(typeof(DirectoryCheckRequest))]
[JsonSerializable(typeof(DirectoryCheck))]
[JsonSerializable(typeof(ServerMessage))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, ServerMessage[]>))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(SettingsSectionView<DeploymentSettings>))]
[JsonSerializable(typeof(SettingsSectionUpdate<DeploymentSettings>))]
[JsonSerializable(typeof(SettingsSectionView<MachineSettings>))]
[JsonSerializable(typeof(SettingsSectionUpdate<MachineSettings>))]
[JsonSerializable(typeof(SettingsSectionView<LdapSettings>))]
[JsonSerializable(typeof(SettingsSectionUpdate<LdapSettings>))]
[JsonSerializable(typeof(SettingsSectionView<OidcSettings>))]
[JsonSerializable(typeof(SettingsSectionUpdate<OidcSettings>))]
[JsonSerializable(typeof(SettingsSectionView<ProxySettings>))]
[JsonSerializable(typeof(SettingsSectionUpdate<ProxySettings>))]
[JsonSerializable(typeof(SettingsSectionView<PxeSettings>))]
[JsonSerializable(typeof(SettingsSectionUpdate<PxeSettings>))]
[JsonSerializable(typeof(SettingsSectionView<LoggingSettings>))]
[JsonSerializable(typeof(SettingsSectionUpdate<LoggingSettings>))]
[JsonSerializable(typeof(SettingsOverview))]
[JsonSerializable(typeof(ReauthenticateRequest))]
[JsonSerializable(typeof(ReauthenticationToken))]
[JsonSerializable(typeof(LdapTestRequest))]
[JsonSerializable(typeof(LdapTestResult))]
[JsonSerializable(typeof(OidcTestRequest))]
[JsonSerializable(typeof(OidcTestResult))]
[JsonSerializable(typeof(IReadOnlyList<PxeHostInterfaces>))]
[JsonSerializable(typeof(AgentBinaryView))]
[JsonSerializable(typeof(IReadOnlyList<LogFileView>))]
[JsonSerializable(typeof(ConsoleLogoView))]
[JsonSerializable(typeof(SettingsSectionView<CertificateSettings>))]
[JsonSerializable(typeof(SettingsSectionUpdate<CertificateSettings>))]
[JsonSerializable(typeof(CertificateView))]
[JsonSerializable(typeof(CertificateUpload))]
[JsonSerializable(typeof(CertificateGenerate))]
[JsonSerializable(typeof(IReadOnlyList<FactView>))]
[JsonSerializable(typeof(AnswerInputsRequest))]
[JsonSerializable(typeof(ContinueRunRequest))]
[JsonSerializable(typeof(IReadOnlyList<ResolvedValue>))]
[JsonSerializable(typeof(RuleView))]
[JsonSerializable(typeof(IReadOnlyList<RuleView>))]
[JsonSerializable(typeof(RuleView[]))]
[JsonSerializable(typeof(SaveRuleRequest))]
[JsonSerializable(typeof(ReorderRulesRequest))]
[JsonSerializable(typeof(MachineRoleView))]
[JsonSerializable(typeof(IReadOnlyList<MachineRoleView>))]
[JsonSerializable(typeof(MachineRoleView[]))]
[JsonSerializable(typeof(SaveMachineRoleRequest))]
[JsonSerializable(typeof(AccountView))]
[JsonSerializable(typeof(IReadOnlyList<AccountView>))]
[JsonSerializable(typeof(SaveAccountRequest))]
[JsonSerializable(typeof(AccountsRemovedEvent))]
public sealed partial class DdtJsonContext : JsonSerializerContext;

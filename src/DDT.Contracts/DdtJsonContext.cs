using System.Text.Json.Serialization;
using DDT.Contracts.About;
using DDT.Contracts.Authentication;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;

namespace DDT.Contracts;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(CurrentUser))]
[JsonSerializable(typeof(ChangePasswordRequest))]
[JsonSerializable(typeof(TwoFactorEnrollment))]
[JsonSerializable(typeof(TwoFactorVerifyRequest))]
[JsonSerializable(typeof(RecoveryCodes))]
[JsonSerializable(typeof(MachineSummary))]
[JsonSerializable(typeof(IReadOnlyList<MachineSummary>))]
[JsonSerializable(typeof(IReadOnlyList<MachineLogEntry>))]
[JsonSerializable(typeof(ImageSummary))]
[JsonSerializable(typeof(IReadOnlyList<ImageSummary>))]
[JsonSerializable(typeof(CreateImageUploadRequest))]
[JsonSerializable(typeof(ImageUploadSession))]
[JsonSerializable(typeof(IReadOnlyList<ImageUploadSession>))]
[JsonSerializable(typeof(AssignImageRequest))]
[JsonSerializable(typeof(DeploymentOptionsView))]
[JsonSerializable(typeof(AboutInfo))]
public sealed partial class DdtJsonContext : JsonSerializerContext;

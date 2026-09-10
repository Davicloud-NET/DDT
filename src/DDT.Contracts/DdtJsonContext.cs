using System.Text.Json.Serialization;
using DDT.Contracts.Authentication;

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
public sealed partial class DdtJsonContext : JsonSerializerContext;

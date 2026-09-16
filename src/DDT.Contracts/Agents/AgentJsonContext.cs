using System.Text.Json.Serialization;

namespace DDT.Contracts.Agents;

// Separate from DdtJsonContext so the NativeAOT agent carries metadata only for what it sends.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AgentRegistration))]
[JsonSerializable(typeof(AgentRegistrationResult))]
[JsonSerializable(typeof(AgentNextResult))]
[JsonSerializable(typeof(AgentLogBatch))]
[JsonSerializable(typeof(AgentSignInRequest))]
[JsonSerializable(typeof(AgentSignInResult))]
public sealed partial class AgentJsonContext : JsonSerializerContext;

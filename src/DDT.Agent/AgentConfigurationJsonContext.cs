using System.Text.Json.Serialization;

namespace DDT.Agent;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip)]
[JsonSerializable(typeof(AgentConfiguration))]
public sealed partial class AgentConfigurationJsonContext : JsonSerializerContext;

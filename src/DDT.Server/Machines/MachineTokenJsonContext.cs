using System.Text.Json.Serialization;

namespace DDT.Server.Machines;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MachineTokenPayload))]
internal sealed partial class MachineTokenJsonContext : JsonSerializerContext;

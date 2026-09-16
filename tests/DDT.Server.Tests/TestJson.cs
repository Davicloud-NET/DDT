using System.Text.Json;
using System.Text.Json.Serialization;

namespace DDT.Server.Tests;

// The server writes enums as strings, as the SPA and the agent expect.
internal static class TestJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

using Xunit;

namespace DDT.Agent.Tests;

public sealed class AgentOptionsTests
{
    [Fact]
    public void AcceptsTheArgumentAnOlderAgentPassesLast()
    {
        // What an agent from an older boot image passes to the newer agent it starts.
        Assert.True(AgentOptions.TryParse(["--server", "https://ddt.example:7152", AgentOptions.NoUpdateArgument], out AgentOptions? options, out string error), error);
        Assert.True(options!.NoUpdate);
    }

    [Fact]
    public void ReadsAnAgentJsonWrittenWhileEnrollmentTokensExisted()
    {
        string path = Path.Combine(Path.GetTempPath(), $"agent-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "serverUrl": "https://ddt.example:7152", "enrollmentToken": "ddt1.x.y", "rootCertificate": null }""");

        try
        {
            Assert.True(AgentOptions.TryParse(["--config", path], out AgentOptions? options, out string error), error);
            Assert.Equal(new Uri("https://ddt.example:7152"), options!.ServerUrl);
            Assert.False(options.NoUpdate);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

using Microsoft.AspNetCore.Hosting;

namespace DDT.Server.Tests;

public sealed class AgentReleaseApplication : DdtApplication
{
    public string BinaryPath { get; } = Path.Combine(Path.GetTempPath(), $"ddt-agent-release-{Guid.NewGuid():N}.exe");

    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("DDT:Agent:BinaryPath", BinaryPath);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            File.Delete(BinaryPath);
        }
    }
}

namespace DDT.Server.Machines;

public sealed class AgentReleaseOptions
{
    public const string SectionName = "DDT:Agent";

    // The published agent every netbooting machine switches to. Empty means agent/ddt-agent.exe in the store.
    public string BinaryPath { get; init; } = string.Empty;
}

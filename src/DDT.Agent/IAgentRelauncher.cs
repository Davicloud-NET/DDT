namespace DDT.Agent;

public interface IAgentRelauncher
{
    // Runs the agent at path in this console and returns its exit code once it has ended.
    Task<int> RunAsync(string path, IReadOnlyList<string> arguments);
}

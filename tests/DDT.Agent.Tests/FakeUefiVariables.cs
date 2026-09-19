using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Firmware variables in memory. Writes and Deletes list the names written and deleted, in order.
internal sealed class FakeUefiVariables : IUefiVariables
{
    public Dictionary<string, byte[]> Values { get; } = new(StringComparer.Ordinal);

    public List<string> Writes { get; } = [];

    public List<string> Deletes { get; } = [];

    public byte[]? Read(string name) => Values.TryGetValue(name, out byte[]? value) ? value : null;

    public void Write(string name, byte[] value)
    {
        Writes.Add(name);
        Values[name] = value;
    }

    public void Delete(string name)
    {
        Deletes.Add(name);
        Values.Remove(name);
    }
}

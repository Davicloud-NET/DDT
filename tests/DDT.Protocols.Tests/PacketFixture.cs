using System.Reflection;

namespace DDT.Protocols.Tests;

public static class PacketFixture
{
    private static readonly string s_root = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
        "Fixtures");

    public static byte[] Load(string protocol, string name) =>
        File.ReadAllBytes(Path.Combine(s_root, protocol, name + ".bin"));
}

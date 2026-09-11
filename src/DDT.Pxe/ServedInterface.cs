using System.Net;

namespace DDT.Pxe;

public sealed record ServedInterface(int Index, string Name, IPAddress Address);

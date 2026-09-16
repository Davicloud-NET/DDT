using Microsoft.AspNetCore.SignalR;

namespace DDT.Server.Live;

// Server to client only. Clients receive small change events and patch or refetch their queries.
public sealed class LiveHub : Hub;

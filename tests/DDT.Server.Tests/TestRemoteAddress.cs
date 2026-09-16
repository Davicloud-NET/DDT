using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace DDT.Server.Tests;

// The in-memory server has no client address, so every request would share one rate limit partition. A
// test that names an address in this header gets its own.
internal sealed class TestRemoteAddress : IStartupFilter
{
    public const string Header = "X-Test-Remote-Address";

    private static int s_next;

    public static string Unique()
    {
        int next = Interlocked.Increment(ref s_next);

        return $"10.{(next >> 16) & 0xFF}.{(next >> 8) & 0xFF}.{next & 0xFF}";
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (IPAddress.TryParse(context.Request.Headers[Header], out IPAddress? address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            return nextMiddleware(context);
        });

        next(app);
    };
}

using System.Net;

namespace DDT.Server.Tests;

// A body sent without Content-Length, as a client streaming it chunked does.
internal sealed class UnknownLengthContent(byte[] content) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(content).AsTask();

    protected override bool TryComputeLength(out long length)
    {
        length = 0;

        return false;
    }
}

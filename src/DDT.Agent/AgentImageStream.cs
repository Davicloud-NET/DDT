namespace DDT.Agent;

// The body of an image download starting at Offset, of a file TotalLength bytes long. Disposing it ends the
// response and frees its connection.
public sealed class AgentImageStream(Stream content, long offset, long totalLength, IDisposable? response = null) : IAsyncDisposable
{
    public Stream Content { get; } = content;

    public long Offset { get; } = offset;

    public long TotalLength { get; } = totalLength;

    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync().ConfigureAwait(false);
        response?.Dispose();
    }
}

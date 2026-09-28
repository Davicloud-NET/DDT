// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text.Json;

namespace DDT.ConsoleProtocol;

// Messages over a stream, each as its length in 4 bytes, little-endian, followed by that many bytes of JSON in UTF-8.
// Several threads may send at once; one reads. The channel does not own the stream.
public sealed class ConsoleChannel(Stream stream) : IDisposable
{
    // A whole state or a batch of log lines takes a few kilobytes. A length far beyond that is not a console message,
    // and must not make the other end allocate it.
    public const int MaxMessageBytes = 4 * 1024 * 1024;

    private readonly SemaphoreSlim _sending = new(1, 1);

    public async Task SendAsync(ConsoleMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(message, ConsoleProtocolJsonContext.Default.ConsoleMessage);

        if (json.Length > MaxMessageBytes)
        {
            throw new ConsoleProtocolException($"A message of {json.Length} bytes is larger than a console message may be.");
        }

        byte[] frame = new byte[sizeof(int) + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, json.Length);
        json.CopyTo(frame.AsSpan(sizeof(int)));

        await _sending.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sending.Release();
        }
    }

    // The next message, or null when the stream ended between two messages. A stream that ends inside a message, a
    // length out of range, and anything that is not a message of this protocol throw ConsoleProtocolException.
    public async Task<ConsoleMessage?> ReceiveAsync(CancellationToken cancellationToken)
    {
        byte[] header = new byte[sizeof(int)];
        int read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

        if (read == 0)
        {
            return null;
        }

        if (read < header.Length)
        {
            throw new ConsoleProtocolException("The stream ended inside a message.");
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);

        if (length is <= 0 or > MaxMessageBytes)
        {
            throw new ConsoleProtocolException($"A message of {length} bytes is not a console message.");
        }

        byte[] json = new byte[length];

        if (await stream.ReadAtLeastAsync(json, length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false) < length)
        {
            throw new ConsoleProtocolException("The stream ended inside a message.");
        }

        try
        {
            return JsonSerializer.Deserialize(json, ConsoleProtocolJsonContext.Default.ConsoleMessage)
                ?? throw new ConsoleProtocolException("The message is empty.");
        }
        catch (JsonException exception)
        {
            throw new ConsoleProtocolException($"The message is not one of this protocol: {exception.Message}", exception);
        }
        catch (NotSupportedException exception)
        {
            // A message without its type, which names no message the protocol knows.
            throw new ConsoleProtocolException($"The message is not one of this protocol: {exception.Message}", exception);
        }
    }

    public void Dispose() => _sending.Dispose();
}

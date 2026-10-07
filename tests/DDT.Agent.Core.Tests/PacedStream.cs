// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Tests;

// A slow connection. It returns its first part right away and every later part only when the test releases it, so the
// test can move the clock while a read waits. Each part must fit in one read.
internal sealed class PacedStream(params byte[][] parts) : Stream
{
    private readonly Queue<byte[]> _parts = new(parts);
    private readonly SemaphoreSlim _waiting = new(0);
    private readonly SemaphoreSlim _released = new(0);
    private bool _started;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    // Completes once a read waits for the next part.
    public Task WaitingAsync(CancellationToken cancellationToken) => _waiting.WaitAsync(cancellationToken);

    public void Release() => _released.Release();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_parts.TryDequeue(out byte[]? part))
        {
            return 0;
        }

        if (_started)
        {
            _waiting.Release();
            await _released.WaitAsync(cancellationToken);

            // A release can reach a wait that was cancelled a moment before.
            cancellationToken.ThrowIfCancellationRequested();
        }

        _started = true;
        part.CopyTo(buffer);

        return part.Length;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _waiting.Dispose();
            _released.Dispose();
        }

        base.Dispose(disposing);
    }
}

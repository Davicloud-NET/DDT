// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;

namespace DDT.Agent.Deployment;

// A part file that keeps what arrived when the agent stops. A part file longer than the file can't be the start of it.
public sealed class FileDownloadSink : IDownloadSink, IAsyncDisposable
{
    private const int BufferSize = 1024 * 1024;

    private readonly FileStream _file;

    public FileDownloadSink(string path, long sizeBytes)
    {
        _file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 0, useAsync: true);

        if (_file.Length > sizeBytes)
        {
            _file.SetLength(0);
        }
    }

    public long Length => _file.Length;

    public async Task HashExistingAsync(IncrementalHash hash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hash);

        byte[] buffer = new byte[BufferSize];
        _file.Position = 0;
        int read;

        while ((read = await _file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        _file.Position = _file.Length;
        await _file.WriteAsync(data, cancellationToken).ConfigureAwait(false);
    }

    public void Restart() => _file.SetLength(0);

    public ValueTask DisposeAsync() => _file.DisposeAsync();
}

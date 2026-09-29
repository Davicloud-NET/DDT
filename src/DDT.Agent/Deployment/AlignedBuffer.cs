// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.Deployment;

// Native memory aligned to 4 KiB. Unbuffered disk I/O needs that, whatever the sector size.
public sealed unsafe class AlignedBuffer : IDisposable
{
    public const int Alignment = 4096;

    private void* _memory;

    public AlignedBuffer(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        _memory = NativeMemory.AlignedAlloc((nuint)length, Alignment);
        NativeMemory.Clear(_memory, (nuint)length);
        Length = length;
    }

    public int Length { get; }

    public Span<byte> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(_memory is null, this);

            return new Span<byte>(_memory, Length);
        }
    }

    public void Dispose()
    {
        if (_memory is not null)
        {
            NativeMemory.AlignedFree(_memory);
            _memory = null;
        }
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace DDT.Core.Wim;

// Holds one wimlib operation's progress target, its cancellation and what its callback saw. The callback reaches it
// through a GCHandle passed as wimlib's progress context.
internal sealed unsafe class WimProgressState : IDisposable
{
    private const int ExtractStreamsMessage = 4;
    private const int WriteStreamsMessage = 12;
    private const int HandleErrorMessage = 31;
    private const int Continue = 0;
    private const int Abort = 1;

    private readonly IProgress<WimProgress>? _progress;
    private readonly CancellationToken _cancellationToken;
    private GCHandle<WimProgressState> _handle;
    private Exception? _failure;

    public WimProgressState(IProgress<WimProgress>? progress, CancellationToken cancellationToken)
    {
        _progress = progress;
        _cancellationToken = cancellationToken;
        _handle = new GCHandle<WimProgressState>(this);
    }

    public static delegate* unmanaged[Cdecl]<int, void*, nint, int> Callback => &OnProgress;

    public nint Context => GCHandle<WimProgressState>.ToIntPtr(_handle);

    // The last file wimlib reported an error for, with that error's code.
    public string? ErrorPath { get; private set; }

    public int ErrorPathCode { get; private set; }

    public void Dispose() => _handle.Dispose();

    // wimlib answers an abort from the callback with error 76. This turns it back into what caused the abort.
    public void ThrowIfAborted()
    {
        if (_failure is not null)
        {
            ExceptionDispatchInfo.Throw(_failure);
        }

        _cancellationToken.ThrowIfCancellationRequested();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OnProgress(int message, void* info, nint context) =>
        GCHandle<WimProgressState>.FromIntPtr(context).Target.Receive(message, info);

    // An exception mustn't escape into wimlib. So when a progress handler throws, the exception is kept and the
    // operation is aborted.
    private int Receive(int message, void* info)
    {
        try
        {
            switch (message)
            {
                case ExtractStreamsMessage when info is not null:
                    ExtractProgressInfo* extract = (ExtractProgressInfo*)info;
                    _progress?.Report(new WimProgress((long)extract->CompletedBytes, (long)extract->TotalBytes));
                    break;

                case WriteStreamsMessage when info is not null:
                    WriteStreamsProgressInfo* write = (WriteStreamsProgressInfo*)info;
                    _progress?.Report(new WimProgress((long)write->CompletedBytes, (long)write->TotalBytes));
                    break;

                case HandleErrorMessage when info is not null:
                    // wimlib fails the operation with this error once the callback returns.
                    HandleErrorProgressInfo* error = (HandleErrorProgressInfo*)info;
                    ErrorPath = Marshal.PtrToStringUni(error->Path);
                    ErrorPathCode = error->ErrorCode;
                    return Continue;
            }
        }
        catch (Exception exception)
        {
            _failure = exception;
        }

        return _failure is not null || _cancellationToken.IsCancellationRequested ? Abort : Continue;
    }
}

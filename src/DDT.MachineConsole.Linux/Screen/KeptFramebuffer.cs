// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.LinuxFramebuffer;
using Avalonia.LinuxFramebuffer.Output;
using Avalonia.Platform;
using Avalonia.Platform.Surfaces;

namespace DDT.MachineConsole.Screen;

// Avalonia draws into memory, and the picture reaches the screen only while the console's terminal is in front. Away,
// nothing is drawn over the other terminal; back, the whole picture is copied at once.
internal sealed unsafe class KeptFramebuffer : IOutputBackend, IFramebufferPlatformSurface, IDisposable
{
    private readonly FbdevOutput _screen;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PixelSize _size;
    private readonly int _rowBytes;
    private readonly PixelFormat _format;
    private readonly Vector _dpi;
    private byte* _picture;
    private bool _shown = true;

    public KeptFramebuffer(string device)
    {
        _screen = new FbdevOutput(device);

        using (ILockedFramebuffer screen = _screen.Lock(default, out _))
        {
            _size = screen.Size;
            _rowBytes = screen.RowBytes;
            _format = screen.Format;
            _dpi = screen.Dpi;
        }

        _picture = (byte*)NativeMemory.AllocZeroed((nuint)(_rowBytes * _size.Height));
    }

    public PixelSize PixelSize => _screen.PixelSize;

    public double Scaling
    {
        get => _screen.Scaling;
        set => _screen.Scaling = value;
    }

    public IFramebufferRenderTarget CreateFramebufferRenderTarget() => new Target(this);

    // From any thread. Back in front, the screen gets the whole picture.
    public void Show(bool shown)
    {
        _gate.Wait();

        try
        {
            _shown = shown;

            if (shown)
            {
                Copy();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Wait();

        try
        {
            if (_picture is not null)
            {
                NativeMemory.Free(_picture);
                _picture = null;
                _screen.Dispose();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // The picture keeps what Avalonia drew before, so only what changed is drawn again.
    private ILockedFramebuffer Lock(out FramebufferLockProperties properties)
    {
        _gate.Wait();
        properties = new FramebufferLockProperties(PreviousFrameIsRetained: true);

        return new LockedFramebuffer((nint)_picture, _size, _rowBytes, _dpi, _format, AlphaFormat.Opaque, Unlock);
    }

    private void Unlock()
    {
        try
        {
            if (_shown)
            {
                Copy();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Copy()
    {
        if (_picture is null)
        {
            return;
        }

        using ILockedFramebuffer screen = _screen.Lock(default, out _);
        int row = Math.Min(_rowBytes, screen.RowBytes);
        int rows = Math.Min(_size.Height, screen.Size.Height);

        for (int y = 0; y < rows; y++)
        {
            Buffer.MemoryCopy(_picture + (y * _rowBytes), (byte*)screen.Address + (y * screen.RowBytes), row, row);
        }
    }

    private sealed class Target(KeptFramebuffer output) : IFramebufferRenderTarget
    {
        public ILockedFramebuffer Lock(IRenderTarget.RenderTargetSceneInfo sceneInfo, out FramebufferLockProperties properties) =>
            output.Lock(out properties);

        public void Dispose()
        {
        }
    }
}

// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DDT.Core.Wim;

// wimlib through libwim-15.dll, which must sit where the runtime finds native libraries. Each operation runs on
// its own thread with its own WIMStruct. wimlib's global settings can be made once per process, so every
// instance has to ask for the same ones.
[SupportedOSPlatform("windows")]
public sealed class WimLibrary : IWimLibrary
{
    private static readonly Lock s_initializationLock = new();
    private static bool s_initialized;
    private static bool s_strict;
    private static string? s_errorLogPath;

    private readonly bool _strict;

    // Strict fails initialization when the privileges to capture and apply security descriptors are missing,
    // and fails an apply that cannot set an ACL or a symbolic link exactly. The error log receives wimlib's
    // warnings, which it only prints and never returns.
    public WimLibrary(bool strict, string? errorLogPath)
    {
        _strict = strict;
        Initialize(strict, errorLogPath is null ? null : Path.GetFullPath(errorLogPath));
    }

    public Task ApplyAsync(
        string wimPath,
        int index,
        string targetDirectory,
        IProgress<WimProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(wimPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        ArgumentException.ThrowIfNullOrEmpty(targetDirectory);

        int flags = _strict ? WimNativeMethods.ExtractStrictAcls | WimNativeMethods.ExtractStrictSymlinks : 0;

        return Task.Run(() => Apply(wimPath, index, targetDirectory, flags, progress, cancellationToken), cancellationToken);
    }

    public Task CaptureAsync(
        string sourceDirectory,
        string wimPath,
        string imageName,
        WimCompression compression,
        IProgress<WimProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceDirectory);
        ArgumentException.ThrowIfNullOrEmpty(wimPath);
        ArgumentNullException.ThrowIfNull(imageName);

        int compressionType = CompressionType(compression);

        return Task.Run(
            () => Capture(sourceDirectory, wimPath, imageName, compressionType, WriteFlags(compression), progress, cancellationToken),
            cancellationToken);
    }

    public Task ExportAsync(
        string sourceWimPath,
        int index,
        string destinationWimPath,
        WimCompression compression,
        IProgress<WimProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceWimPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        ArgumentException.ThrowIfNullOrEmpty(destinationWimPath);

        int compressionType = CompressionType(compression);

        return Task.Run(
            () => Export(sourceWimPath, index, destinationWimPath, compressionType, WriteFlags(compression), progress, cancellationToken),
            cancellationToken);
    }

    private static void Initialize(bool strict, string? errorLogPath)
    {
        lock (s_initializationLock)
        {
            if (s_initialized)
            {
                if (strict != s_strict || !string.Equals(errorLogPath, s_errorLogPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new WimLibraryException(
                        $"wimlib is set up for this process {Settings(s_strict, s_errorLogPath)}, " +
                        $"so it cannot also run {Settings(strict, errorLogPath)}.");
                }

                return;
            }

            // The progress structures are read at their 64-bit offsets.
            if (!Environment.Is64BitProcess)
            {
                throw new WimLibraryException("wimlib can only be used from a 64-bit process.");
            }

            try
            {
                if (errorLogPath is not null)
                {
                    WimNativeMethods.SetPrintErrors(true);

                    int logCode = WimNativeMethods.SetErrorFileByName(errorLogPath);

                    if (logCode != 0)
                    {
                        throw Failure(logCode, $"The wimlib error log {errorLogPath} could not be opened", null);
                    }
                }

                int initCode = WimNativeMethods.GlobalInit(
                    strict ? WimNativeMethods.InitStrictCapturePrivileges | WimNativeMethods.InitStrictApplyPrivileges : 0);

                if (initCode != 0)
                {
                    throw Failure(initCode, "wimlib could not be initialized", null);
                }
            }
            catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException)
            {
                throw new WimLibraryException($"{WimNativeMethods.LibraryFileName} could not be loaded: {exception.Message}", exception);
            }

            s_initialized = true;
            s_strict = strict;
            s_errorLogPath = errorLogPath;
        }
    }

    private static string Settings(bool strict, string? errorLogPath) =>
        $"{(strict ? "with" : "without")} strict privileges and " +
        (errorLogPath is null ? "without an error log" : $"with the error log {errorLogPath}");

    private static unsafe void Apply(
        string wimPath,
        int index,
        string targetDirectory,
        int flags,
        IProgress<WimProgress>? progress,
        CancellationToken cancellationToken)
    {
        string failure = $"Image {index} of {wimPath} could not be applied to {targetDirectory}";
        using WimProgressState state = new(progress, cancellationToken);
        nint wim = 0;

        try
        {
            Check(WimNativeMethods.OpenWim(wimPath, 0, out wim), failure, state);
            WimNativeMethods.RegisterProgressFunction(wim, WimProgressState.Callback, state.Context);
            Check(WimNativeMethods.ExtractImage(wim, index, targetDirectory, flags), failure, state);
        }
        finally
        {
            WimNativeMethods.Free(wim);
        }
    }

    private static unsafe void Capture(
        string sourceDirectory,
        string wimPath,
        string imageName,
        int compressionType,
        int writeFlags,
        IProgress<WimProgress>? progress,
        CancellationToken cancellationToken)
    {
        string failure = $"{sourceDirectory} could not be captured into {wimPath}";
        using WimProgressState state = new(progress, cancellationToken);
        nint wim = 0;

        try
        {
            Check(WimNativeMethods.CreateNewWim(compressionType, out wim), failure, state);
            WimNativeMethods.RegisterProgressFunction(wim, WimProgressState.Callback, state.Context);

            // NORPFIX keeps absolute link targets as they are, so an image captured from another drive letter
            // still points at C: after it is applied to W:.
            Check(
                WimNativeMethods.AddImage(
                    wim,
                    sourceDirectory,
                    imageName,
                    null,
                    WimNativeMethods.AddWindowsConfiguration | WimNativeMethods.AddNoReparsePointFix),
                failure,
                state);

            Check(WimNativeMethods.Write(wim, wimPath, WimNativeMethods.AllImages, writeFlags, 0), failure, state);
        }
        finally
        {
            WimNativeMethods.Free(wim);
        }
    }

    private static unsafe void Export(
        string sourceWimPath,
        int index,
        string destinationWimPath,
        int compressionType,
        int writeFlags,
        IProgress<WimProgress>? progress,
        CancellationToken cancellationToken)
    {
        string failure = $"Image {index} of {sourceWimPath} could not be exported to {destinationWimPath}";
        using WimProgressState state = new(progress, cancellationToken);
        nint source = 0;
        nint destination = 0;

        try
        {
            Check(WimNativeMethods.OpenWim(sourceWimPath, 0, out source), failure, state);
            Check(WimNativeMethods.CreateNewWim(compressionType, out destination), failure, state);

            // wimlib reports the export's progress on the WIM being written.
            WimNativeMethods.RegisterProgressFunction(destination, WimProgressState.Callback, state.Context);
            Check(WimNativeMethods.ExportImage(source, index, destination, null, null, 0), failure, state);
            Check(WimNativeMethods.Write(destination, destinationWimPath, WimNativeMethods.AllImages, writeFlags, 0), failure, state);
        }
        finally
        {
            WimNativeMethods.Free(destination);
            WimNativeMethods.Free(source);
        }
    }

    private static int CompressionType(WimCompression compression) => compression switch
    {
        WimCompression.None => WimNativeMethods.CompressionNone,
        WimCompression.Xpress => WimNativeMethods.CompressionXpress,
        WimCompression.Lzx => WimNativeMethods.CompressionLzx,
        WimCompression.Lzms => WimNativeMethods.CompressionLzms,
        _ => throw new ArgumentOutOfRangeException(nameof(compression), compression, "Unknown compression."),
    };

    private static int WriteFlags(WimCompression compression) =>
        compression == WimCompression.Lzms ? WimNativeMethods.WriteSolid : 0;

    private static void Check(int code, string failure, WimProgressState state)
    {
        if (code == 0)
        {
            return;
        }

        if (code == WimNativeMethods.ErrorAbortedByProgress)
        {
            state.ThrowIfAborted();
        }

        throw Failure(code, failure, state.ErrorPathCode == code ? state.ErrorPath : null);
    }

    private static WimLibraryException Failure(int code, string failure, string? path)
    {
        string reason = Marshal.PtrToStringUni(WimNativeMethods.GetErrorString(code))?.TrimEnd('.') ?? "unknown error";
        string message = $"{failure}: {reason} (wimlib error {code}).";

        return new WimLibraryException(path is null ? message : $"{message} The failing path is {path}.", code, path);
    }
}

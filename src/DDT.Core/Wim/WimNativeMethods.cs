// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DDT.Core.Wim;

// libwim-15 from the ManagedWimLib package (wimlib 1.14.4). Returned strings are static or borrowed by wimlib,
// so they come back as pointers and are never freed here.
[SupportedOSPlatform("windows")]
internal static unsafe partial class WimNativeMethods
{
    public const string LibraryFileName = "libwim-15.dll";

    public const int AllImages = -1;

    public const int ErrorAbortedByProgress = 76;

    public const int InitStrictCapturePrivileges = 0x4;
    public const int InitStrictApplyPrivileges = 0x8;

    public const int AddNoReparsePointFix = 0x200;
    public const int AddWindowsConfiguration = 0x800;

    public const int ExtractStrictAcls = 0x80;
    public const int ExtractStrictSymlinks = 0x8000;

    public const int WriteSolid = 0x1000;

    public const int CompressionNone = 0;
    public const int CompressionXpress = 1;
    public const int CompressionLzx = 2;
    public const int CompressionLzms = 3;

    private const string Library = "libwim-15";

    [LibraryImport(Library, EntryPoint = "wimlib_global_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int GlobalInit(int initFlags);

    [LibraryImport(Library, EntryPoint = "wimlib_set_print_errors")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SetPrintErrors([MarshalAs(UnmanagedType.U1)] bool showMessages);

    [LibraryImport(Library, EntryPoint = "wimlib_set_error_file_by_name", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SetErrorFileByName(string path);

    [LibraryImport(Library, EntryPoint = "wimlib_get_error_string")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint GetErrorString(int code);

    [LibraryImport(Library, EntryPoint = "wimlib_open_wim", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int OpenWim(string wimFile, int openFlags, out nint wim);

    [LibraryImport(Library, EntryPoint = "wimlib_create_new_wim")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int CreateNewWim(int compressionType, out nint wim);

    [LibraryImport(Library, EntryPoint = "wimlib_add_image", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int AddImage(nint wim, string source, string? name, string? configFile, int addFlags);

    [LibraryImport(Library, EntryPoint = "wimlib_export_image", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int ExportImage(
        nint sourceWim,
        int sourceImage,
        nint destinationWim,
        string? destinationName,
        string? destinationDescription,
        int exportFlags);

    [LibraryImport(Library, EntryPoint = "wimlib_extract_image", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int ExtractImage(nint wim, int image, string target, int extractFlags);

    [LibraryImport(Library, EntryPoint = "wimlib_write", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Write(nint wim, string path, int image, int writeFlags, uint numThreads);

    [LibraryImport(Library, EntryPoint = "wimlib_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Free(nint wim);

    [LibraryImport(Library, EntryPoint = "wimlib_register_progress_function")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void RegisterProgressFunction(
        nint wim,
        delegate* unmanaged[Cdecl]<int, void*, nint, int> progressFunction,
        nint progressContext);
}

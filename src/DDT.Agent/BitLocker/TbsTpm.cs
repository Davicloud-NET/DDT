// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;

namespace DDT.Agent.BitLocker;

// Asks the TPM Base Services whether there is a TPM. tbs.dll is loaded by hand, because a boot image without
// WinPE-SecureStartup lacks it, and that means no TPM rather than an agent that cannot start.
public sealed unsafe class TbsTpm : ITpm
{
    public bool IsPresent()
    {
        if (!NativeLibrary.TryLoad("tbs.dll", typeof(TbsTpm).Assembly, DllImportSearchPath.System32, out IntPtr library))
        {
            return false;
        }

        try
        {
            if (!NativeLibrary.TryGetExport(library, "Tbsi_GetDeviceInfo", out IntPtr export))
            {
                return false;
            }

            // TBS_SUCCESS; TBS_E_TPM_NOT_FOUND and every other answer mean no usable TPM.
            DeviceInfo info = default;
            delegate* unmanaged[Stdcall]<uint, DeviceInfo*, uint> getDeviceInfo = (delegate* unmanaged[Stdcall]<uint, DeviceInfo*, uint>)export;

            return getDeviceInfo((uint)sizeof(DeviceInfo), &info) == 0;
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    // TPM_DEVICE_INFO.
    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfo
    {
        public uint StructVersion;
        public uint TpmVersion;
        public uint TpmInterfaceType;
        public uint TpmImpRevision;
    }
}

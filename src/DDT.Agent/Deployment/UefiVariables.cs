// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace DDT.Agent.Deployment;

// The EFI global variables through the Windows firmware variable functions. They need SeSystemEnvironmentPrivilege,
// which Windows PE's SYSTEM account holds but has to enable first.
public sealed class UefiVariables : IUefiVariables
{
    private const string GlobalVariableGuid = "{8BE4DF61-93CA-11D2-AA0D-00E098032B8C}";

    // NON_VOLATILE | BOOTSERVICE_ACCESS | RUNTIME_ACCESS, which the specification requires for boot variables.
    private const uint BootVariableAttributes = 0x7;

    private const int InitialLength = 1024;
    private const int MaxLength = 1024 * 1024;

    // TOKEN_PRIVILEGES with one LUID_AND_ATTRIBUTES: the count, the LUID at 4 and its attributes at 12.
    private const int TokenPrivilegesLength = 16;

    private bool _privilegeEnabled;

    public unsafe byte[]? Read(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        EnablePrivilege();

        for (int length = InitialLength; length <= MaxLength; length *= 2)
        {
            byte[] buffer = new byte[length];
            uint stored;
            int error;

            fixed (byte* output = buffer)
            {
                stored = FirmwareNativeMethods.GetFirmwareEnvironmentVariableEx(name, GlobalVariableGuid, output, (uint)length, 0);
                error = Marshal.GetLastPInvokeError();
            }

            if (stored > 0)
            {
                return buffer[..(int)stored];
            }

            switch (error)
            {
                case FirmwareNativeMethods.ErrorSuccess:
                    return [];
                case FirmwareNativeMethods.ErrorEnvVarNotFound:
                    return null;
                case DiskNativeMethods.ErrorInsufficientBuffer:
                    continue;
                case FirmwareNativeMethods.ErrorInvalidFunction:
                    throw new DeploymentStepException("This machine did not start in UEFI mode, so its firmware variables cannot be read.");
                default:
                    throw new DeploymentStepException($"The firmware variable {name} cannot be read (Windows error {error}).");
            }
        }

        throw new DeploymentStepException($"The firmware variable {name} is larger than {MaxLength} bytes.");
    }

    public unsafe void Write(string name, byte[] value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        EnablePrivilege();

        fixed (byte* input = value)
        {
            if (!FirmwareNativeMethods.SetFirmwareEnvironmentVariableEx(name, GlobalVariableGuid, input, (uint)value.Length, BootVariableAttributes))
            {
                throw new DeploymentStepException($"The firmware variable {name} cannot be written (Windows error {Marshal.GetLastPInvokeError()}).");
            }
        }
    }

    // A size of zero deletes the variable.
    public unsafe void Delete(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        EnablePrivilege();

        if (!FirmwareNativeMethods.SetFirmwareEnvironmentVariableEx(name, GlobalVariableGuid, null, 0, BootVariableAttributes))
        {
            int error = Marshal.GetLastPInvokeError();

            if (error != FirmwareNativeMethods.ErrorEnvVarNotFound)
            {
                throw new DeploymentStepException($"The firmware variable {name} cannot be deleted (Windows error {error}).");
            }
        }
    }

    private unsafe void EnablePrivilege()
    {
        if (_privilegeEnabled)
        {
            return;
        }

        if (!FirmwareNativeMethods.OpenProcessToken(
            FirmwareNativeMethods.GetCurrentProcess(),
            FirmwareNativeMethods.TokenAdjustPrivileges | FirmwareNativeMethods.TokenQuery,
            out nint token))
        {
            throw new DeploymentStepException($"The agent's access token cannot be opened (Windows error {Marshal.GetLastPInvokeError()}).");
        }

        try
        {
            if (!FirmwareNativeMethods.LookupPrivilegeValue(null, FirmwareNativeMethods.SeSystemEnvironmentName, out long luid))
            {
                throw new DeploymentStepException(
                    $"The privilege to change firmware variables cannot be looked up (Windows error {Marshal.GetLastPInvokeError()}).");
            }

            byte* privileges = stackalloc byte[TokenPrivilegesLength];
            Span<byte> state = new(privileges, TokenPrivilegesLength);
            BinaryPrimitives.WriteUInt32LittleEndian(state, 1);
            BinaryPrimitives.WriteInt64LittleEndian(state[4..], luid);
            BinaryPrimitives.WriteUInt32LittleEndian(state[12..], FirmwareNativeMethods.SePrivilegeEnabled);

            bool adjusted = FirmwareNativeMethods.AdjustTokenPrivileges(token, false, privileges, 0, null, null);
            int error = Marshal.GetLastPInvokeError();

            // AdjustTokenPrivileges succeeds without the privilege and only says so through the last error.
            if (!adjusted || error == FirmwareNativeMethods.ErrorNotAllAssigned)
            {
                throw new DeploymentStepException(error == FirmwareNativeMethods.ErrorNotAllAssigned
                    ? "The agent does not hold the privilege to change firmware variables. Run it as an administrator or in Windows PE."
                    : $"The privilege to change firmware variables cannot be enabled (Windows error {error}).");
            }
        }
        finally
        {
            FirmwareNativeMethods.CloseHandle(token);
        }

        _privilegeEnabled = true;
    }
}

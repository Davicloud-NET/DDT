// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using DDT.Agent.InstalledPhase;

namespace DDT.Agent.WindowsPhase;

// A system-required power request, so the installed Windows does not sleep in a long step. The display may still turn off.
public sealed class PowerRequestKeepAwake(AgentLog log) : IKeepAwake
{
    private const string Reason = "DDT is deploying this machine.";

    public IDisposable Hold()
    {
        // Kept until the request is closed, because Windows shows the reason while it is set.
        nint reason = Marshal.StringToHGlobalUni(Reason);
        nint request = PowerNativeMethods.PowerCreateRequest(new PowerReasonContext(reason));

        if (request == PowerNativeMethods.InvalidHandle)
        {
            log.Warning($"The machine may go to sleep during the run: no power request ({Marshal.GetLastPInvokeError()}).");
            Marshal.FreeHGlobal(reason);

            return new Request(PowerNativeMethods.InvalidHandle, 0);
        }

        if (!PowerNativeMethods.PowerSetRequest(request, PowerNativeMethods.PowerRequestSystemRequired))
        {
            log.Warning($"The machine may go to sleep during the run: the power request was refused ({Marshal.GetLastPInvokeError()}).");
            PowerNativeMethods.CloseHandle(request);
            Marshal.FreeHGlobal(reason);

            return new Request(PowerNativeMethods.InvalidHandle, 0);
        }

        return new Request(request, reason);
    }

    private sealed class Request(nint handle, nint reason) : IDisposable
    {
        private nint _handle = handle;

        public void Dispose()
        {
            if (_handle == PowerNativeMethods.InvalidHandle)
            {
                return;
            }

            PowerNativeMethods.PowerClearRequest(_handle, PowerNativeMethods.PowerRequestSystemRequired);
            PowerNativeMethods.CloseHandle(_handle);
            Marshal.FreeHGlobal(reason);
            _handle = PowerNativeMethods.InvalidHandle;
        }
    }
}

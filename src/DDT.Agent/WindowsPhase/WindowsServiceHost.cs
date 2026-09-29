// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using DDT.Agent.Sequences;

namespace DDT.Agent.WindowsPhase;

// Runs the agent as the DdtSequence service the hand-over registered, by hand, because NativeAOT has no ServiceBase.
// The dispatcher takes over the calling thread, starts ServiceMain on a separate thread and calls HandlerEx with each
// control. Both are called from native code, so no exception may leave them. There's one service per process, so the
// state is static.
public static unsafe class WindowsServiceHost
{
    // The ImagePath the hand-over registers passes this, and nothing else.
    public const string Argument = "--service";

    private static Func<CancellationToken, Task<int>>? s_body;
    private static ServiceLifetime? s_lifetime;
    private static nint s_statusHandle;

    // Runs body as the service until it returns, and returns its exit code. False, with the Windows error as exitCode,
    // when the service control manager did not start this process.
    public static bool TryRun(Func<CancellationToken, Task<int>> body, out int exitCode)
    {
        ArgumentNullException.ThrowIfNull(body);

        s_body = body;
        ServiceTableEntry* table = stackalloc ServiceTableEntry[2];

        fixed (char* name = OfflineServiceRegistration.ServiceName)
        {
            table[0] = new ServiceTableEntry { ServiceName = name, ServiceMain = &ServiceMain };
            table[1] = default;

            if (!ServiceNativeMethods.StartServiceCtrlDispatcher(table))
            {
                exitCode = Marshal.GetLastPInvokeError();

                return false;
            }
        }

        // The dispatcher returns once Stopped is reported, which may be before ServiceMain's thread continues.
        exitCode = s_lifetime?.ExitCode ?? (int)ServiceLifetime.ErrorExceptionInService;

        return true;
    }

    [UnmanagedCallersOnly]
    private static void ServiceMain(uint argumentCount, char** arguments)
    {
        try
        {
            ServiceLifetime lifetime = new(Report);
            s_lifetime = lifetime;

            fixed (char* name = OfflineServiceRegistration.ServiceName)
            {
                s_statusHandle = ServiceNativeMethods.RegisterServiceCtrlHandlerEx(name, &HandlerEx, null);
            }

            if (s_statusHandle == 0)
            {
                // Without a handle the service can never report Stopped, and the dispatcher would wait forever. Ending
                // the process counts as a crash, after which the control manager restarts the service.
                Environment.Exit(Marshal.GetLastPInvokeError());
            }

            _ = lifetime.RunAsync(s_body!).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // RunAsync catches what its body throws, so nothing is left to do here but keep it from native code.
        }
    }

    [UnmanagedCallersOnly]
    private static uint HandlerEx(uint control, uint eventType, void* eventData, void* context)
    {
        try
        {
            return s_lifetime?.Control(control) ?? ServiceLifetime.NoError;
        }
        catch (Exception)
        {
            return ServiceLifetime.NoError;
        }
    }

    private static void Report(ServiceStatus status)
    {
        _ = ServiceNativeMethods.SetServiceStatus(s_statusHandle, &status);
    }
}

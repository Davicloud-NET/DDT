// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using Avalonia;
using DDT.MachineConsole.Agent;
using DDT.MachineConsole.Machine;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole;

// ddt-console.exe --pipe <name>, in Windows PE and as the shell of DDT's session.
public static partial class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ConsoleHost host = new(
            WindowsLanguage.Current(),
            new WindowsPEPower(),
            new CommandPrompt(),
            SessionSignOut.SignOut,
            WindowsSessionAgent.OwnedBySystem);

        if (ConsoleProgram.Prepare(args, host) is { } exit)
        {
            return exit;
        }

        FineTimer();

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Software rendering only, because WinPE has no Direct3D, DXGI, Direct2D, DirectComposition or WARP. WinPE has no
    // Segoe UI either, so text without its own font family uses the console's Archivo.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseWin32()
            .UseSkia()
            .UseHarfBuzz()
            .With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Software],
                CompositionMode = [Win32CompositionMode.RedirectionSurface],
            })
            .With(App.FontOptions);

    // Avalonia times its frames with the system timer. Its 15.6 ms ticks stretch a 16.7 ms frame over two ticks, so
    // motion would run at 32 frames a second. A 1 ms timer resolution for the console's lifetime keeps it at 60.
    private static void FineTimer()
    {
        try
        {
            _ = TimeBeginPeriod(1);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Without winmm.dll the console animates at the coarser rate.
        }
    }

    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static partial uint TimeBeginPeriod(uint milliseconds);
}

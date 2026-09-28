// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using Avalonia;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Agent;

namespace DDT.MachineConsole;

// The agent starts it as ddt-console.exe --pipe <name>. It connects before it opens a window. If no agent accepts it,
// it exits right away, so the agent's text console stays in view. With --session it's the shell of DDT's session.
public static partial class Program
{
    public const int Closed = 0;
    public const int NoPipe = 2;
    public const int Refused = 3;
    public const int NotConnected = 4;

    [STAThread]
    public static int Main(string[] args)
    {
        if (ConsolePipe.NameFrom(args) is not { } pipeName)
        {
            return NoPipe;
        }

        if (ConsolePipe.IsSession(args))
        {
            App.Startup = ConsoleStartup.ForSession(pipeName);
        }
        else
        {
            ClientConnection connection;

            try
            {
                connection = ClientConnection.ConnectAsync(pipeName, $"DDT console {ConsoleBuild.Version}", CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (ConsoleProtocolException)
            {
                return Refused;
            }
            catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException)
            {
                return NotConnected;
            }

            App.Startup = new ConsoleStartup(connection);
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

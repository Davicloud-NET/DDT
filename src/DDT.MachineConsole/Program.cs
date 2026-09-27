// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using Avalonia;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Agent;

namespace DDT.MachineConsole;

// ddt-console.exe --pipe <name>, as the agent starts it. It connects to the agent's pipe first, before it opens a
// window, and ends at once when there is no agent or the agent refuses it, so the agent's text console stays in view.
// With --session it is the shell of DDT's session in the installed Windows, which opens at once and waits for the
// agent, whose service Windows starts alongside.
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

    // Software rendering only: Windows PE has no Direct3D, DXGI, Direct2D, DirectComposition or WARP. Nor has it Segoe
    // UI, so text without a face of its own is set in the console's Archivo.
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

    // Avalonia waits for its next frame with the system's timer, whose steps of 15.6 ms turn the 16.7 ms of a frame at
    // 60 per second into two steps, so everything that moves would move at 32 frames a second. Steps of 1 ms, for as
    // long as the console runs, let it keep 60.
    private static void FineTimer()
    {
        try
        {
            _ = TimeBeginPeriod(1);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Then the console moves at the coarser rate.
        }
    }

    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static partial uint TimeBeginPeriod(uint milliseconds);
}

internal static class ConsoleBuild
{
    public static string Version => typeof(ConsoleBuild).Assembly.GetName().Version?.ToString(3) ?? "unknown";
}

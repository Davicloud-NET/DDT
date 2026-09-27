// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Agent;

namespace DDT.MachineConsole;

// ddt-console.exe --pipe <name>, as the agent starts it. It connects to the agent's pipe first, before it opens a
// window, and ends at once when there is no agent or the agent refuses it, so the agent's text console stays in view.
public static class Program
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
}

internal static class ConsoleBuild
{
    public static string Version => typeof(ConsoleBuild).Assembly.GetName().Version?.ToString(3) ?? "unknown";
}

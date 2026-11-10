// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LinuxFramebuffer;
using Avalonia.Threading;
using DDT.MachineConsole.Agent;
using DDT.MachineConsole.Controls;
using DDT.MachineConsole.Input;
using DDT.MachineConsole.Keyboard;
using DDT.MachineConsole.Machine;
using DDT.MachineConsole.Screen;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole;

// ddt-console --pipe <name> on Linux: in the boot environment, and with --session on a terminal of the installed
// Linux. Without a screen it can draw on it exits before it connects, so the agent's text stays.
public static class Program
{
    // DDT_CONSOLE_MOTION=1 keeps motion on an emulated framebuffer, 0 turns it off everywhere.
    private const string MotionVariable = "DDT_CONSOLE_MOTION";

    public static int Main(string[] args)
    {
        if (Framebuffer.Probe(out string problem) is not { } screen)
        {
            Console.Error.WriteLine($"ddt-console: {problem}");

            return ConsoleExit.NoScreen;
        }

        KeyboardLayout fallback = EmbeddedLayouts.Fallback;
        using EvdevDevices devices = new();
        using KeptFramebuffer output = new(Framebuffer.Device) { Scaling = 1 };
        ScreenKeeper keeper = new(devices, output);
        EvdevInputBackend keyboard = new(new KeyboardTranslator(fallback), devices, keeper);
        ConsoleHost host = new(
            LinuxLanguage.Current(),
            new BootEnvironmentPower(),
            new ShellPrompt(keeper),
            Leave,
            LinuxSessionAgent.OwnedByRoot)
        {
            System = HostSystem.Linux,
            UseKeyboardLayout = name => Use(keyboard, name, fallback),
        };

        if (ConsoleProgram.Prepare(args, host) is { } exit)
        {
            return exit;
        }

        using VirtualTerminal? terminal = VirtualTerminal.Take(out string noTerminal);

        if (terminal is null)
        {
            Console.Error.WriteLine($"ddt-console: {noTerminal} The kernel's text may show over the picture.");
        }

        keeper.Take(terminal);
        Animate(screen);
        using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
        using PosixSignalRegistration hangUp = PosixSignalRegistration.Create(PosixSignal.SIGHUP, Stop);
        using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);

        return BuildAvaloniaApp().StartLinuxDirect(args, output, keyboard);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHarfBuzz()
            .With(new LinuxFramebufferPlatformOptions { Fps = 60 })
            .With(App.FontOptions);

    private static bool Use(EvdevInputBackend keyboard, string? name, KeyboardLayout fallback)
    {
        KeyboardLayout? layout = name is null ? fallback : KeyboardLayouts.Find(name, EmbeddedLayouts.Read);
        keyboard.Use(layout ?? fallback);

        return layout is not null;
    }

    // A DRM driver's framebuffer shows about 20 frames a second, so motion would stutter there.
    private static void Animate(FramebufferInfo screen)
    {
        string? wanted = Environment.GetEnvironmentVariable(MotionVariable);

        if (wanted == "0" || (screen.IsEmulated && wanted != "1"))
        {
            Motion.IsEnabled = false;
            RailModule.Animates = false;
        }
    }

    // F9 in the installed Linux. The agent brings back the terminal that was in front before.
    private static void Leave() => Dispatcher.UIThread.Post(() => Lifetime()?.Shutdown(ConsoleExit.Closed));

    // The terminal goes back to text when Main returns.
    private static void Stop(PosixSignalContext context)
    {
        context.Cancel = true;
        Leave();
    }

    private static IControlledApplicationLifetime? Lifetime() =>
        Application.Current?.ApplicationLifetime as IControlledApplicationLifetime;
}

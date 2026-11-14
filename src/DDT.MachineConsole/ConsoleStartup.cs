// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using DDT.MachineConsole.Agent;
using DDT.MachineConsole.Machine;
using DDT.MachineConsole.Texts;
using DDT.MachineConsole.ViewModels;
using DDT.MachineConsole.Views;

namespace DDT.MachineConsole;

// Opens the console in a window, or as the only view of a screen without windows, and feeds it the agent's messages.
// In DDT's session it opens right away, because nothing else is on the screen, and reconnects each time the agent's
// service restarts.
public sealed class ConsoleStartup
{
    // In DDT's session: the pause between two tries to reach the agent, and how long each try waits for the pipe.
    public static readonly TimeSpan SessionRetry = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan SessionConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly ConsoleHost _host;
    private readonly Func<Localizer, IControlledApplicationLifetime, MainViewModel> _open;
    private readonly bool _fullScreen;

    public ConsoleStartup(ConsoleHost host, IAgentConnection connection)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(connection);

        _host = host;
        _open = (localizer, lifetime) => OpenConnected(host, localizer, lifetime, connection);
        _fullScreen = host.Power.IsWindowsPE;
    }

    private ConsoleStartup(ConsoleHost host, string sessionPipe)
    {
        _host = host;
        _open = (localizer, lifetime) => OpenSession(host, localizer, lifetime, sessionPipe);
        _fullScreen = true;
    }

    public static ConsoleStartup ForSession(ConsoleHost host, string pipeName)
    {
        ArgumentNullException.ThrowIfNull(host);

        return new(host, pipeName);
    }

    public Window Open(App app, IClassicDesktopStyleApplicationLifetime desktop)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(desktop);

        return new MainWindow(Model(desktop), _fullScreen);
    }

    // On a screen without windows, like the Linux framebuffer.
    public Control OpenView(App app, IControlledApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(lifetime);

        return new MainView(Model(lifetime));
    }

    private MainViewModel Model(IControlledApplicationLifetime lifetime)
    {
        MainViewModel model = _open(Localizer.Embedded(_host.StartLanguage), lifetime);

        model.KeyboardLayoutChanged += (_, layout) =>
        {
            if (!_host.UseKeyboardLayout(layout) && layout is not null)
            {
                model.Notice.ShowUnknownLayout(layout);
            }
        };

        return model;
    }

    private static MainViewModel OpenConnected(
        ConsoleHost host,
        Localizer localizer,
        IControlledApplicationLifetime lifetime,
        IAgentConnection connection)
    {
        AgentLink link = new(connection);
        Inbox inbox = new(action => Dispatcher.UIThread.Post(action));

        MainViewModel model = new(
            localizer,
            host.Power,
            host.Prompt,
            (id, answer) => _ = link.AnswerAsync(id, answer),
            () => lifetime.Shutdown(ConsoleExit.Closed))
        {
            System = host.System,
        };

        inbox.Deliver(model.Receive, model.Ended);

        _ = Task.Run(async () => inbox.End(await link.ReadAsync(inbox.Add).ConfigureAwait(false)));

        lifetime.Exit += (_, _) => link.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(1));

        return model;
    }

    // The agent asks here too, like a Pause step does. The link sends each answer to the connection that asked. F9 signs
    // out once the run is over.
    private static MainViewModel OpenSession(
        ConsoleHost host,
        Localizer localizer,
        IControlledApplicationLifetime lifetime,
        string pipeName)
    {
        string program = $"DDT console {ConsoleBuild.Version}";
        CancellationTokenSource stop = new();
        SessionLink link = new(
            async cancellation => await ClientConnection
                .ConnectToSessionAgentAsync(pipeName, program, host.TrustSessionAgent, SessionConnectTimeout, cancellation)
                .ConfigureAwait(false),
            action => Dispatcher.UIThread.Post(action),
            SessionRetry);
        MainViewModel model = new(localizer, host.Power, host.Prompt, link.Answer, host.SignOut, session: true) { System = host.System };

        _ = Task.Run(() => link.ServeAsync(model, stop.Token));
        lifetime.Exit += (_, _) => stop.Cancel();
        KeepDisplayOn(host, model, lifetime);

        return model;
    }

    // Only in DDT's session, where the run goes on in the installed Windows. WinPE is left as it is.
    private static void KeepDisplayOn(ConsoleHost host, MainViewModel model, IControlledApplicationLifetime lifetime)
    {
        if (host.Display is not { } display)
        {
            return;
        }

        RunDisplay screen = new(display);
        model.PropertyChanged += (_, _) => screen.Follow(model.State, model.End.IsEnded);
        lifetime.Exit += (_, _) => screen.Dispose();
    }
}

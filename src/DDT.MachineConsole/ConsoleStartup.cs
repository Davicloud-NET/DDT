// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using DDT.MachineConsole.Agent;
using DDT.MachineConsole.Texts;
using DDT.MachineConsole.ViewModels;
using DDT.MachineConsole.Views;

namespace DDT.MachineConsole;

// Opens the window and feeds it the agent's messages. In DDT's session the window opens right away, because nothing
// else is on the screen. The console reconnects each time the agent's service restarts.
public sealed class ConsoleStartup
{
    // In DDT's session: the pause between two tries to reach the agent, and how long each try waits for the pipe.
    public static readonly TimeSpan SessionRetry = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan SessionConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly ConsoleHost _host;
    private readonly Func<Localizer, IClassicDesktopStyleApplicationLifetime, MainWindow> _open;

    public ConsoleStartup(ConsoleHost host, IAgentConnection connection)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(connection);

        _host = host;
        _open = (localizer, desktop) => OpenConnected(host, localizer, desktop, connection);
    }

    private ConsoleStartup(ConsoleHost host, string sessionPipe)
    {
        _host = host;
        _open = (localizer, desktop) => OpenSession(host, localizer, desktop, sessionPipe);
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

        return _open(Localizer.Embedded(_host.StartLanguage), desktop);
    }

    private static MainWindow OpenConnected(
        ConsoleHost host,
        Localizer localizer,
        IClassicDesktopStyleApplicationLifetime desktop,
        IAgentConnection connection)
    {
        AgentLink link = new(connection);
        Inbox inbox = new(action => Dispatcher.UIThread.Post(action));

        MainViewModel model = new(
            localizer,
            host.Power,
            host.Prompt,
            (id, answer) => _ = link.AnswerAsync(id, answer),
            () => desktop.Shutdown(ConsoleExit.Closed));

        MainWindow window = new(model, fullScreen: host.Power.IsWindowsPE);
        inbox.Deliver(model.Receive, model.Ended);

        _ = Task.Run(async () => inbox.End(await link.ReadAsync(inbox.Add).ConfigureAwait(false)));

        desktop.Exit += (_, _) => link.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(1));

        return window;
    }

    // The agent asks here too, like a Pause step does. The link sends each answer to the connection that asked. F9 signs
    // out once the run is over.
    private static MainWindow OpenSession(ConsoleHost host, Localizer localizer, IClassicDesktopStyleApplicationLifetime desktop, string pipeName)
    {
        string program = $"DDT console {ConsoleBuild.Version}";
        CancellationTokenSource stop = new();
        SessionLink link = new(
            async cancellation => await ClientConnection
                .ConnectToSessionAgentAsync(pipeName, program, host.TrustSessionAgent, SessionConnectTimeout, cancellation)
                .ConfigureAwait(false),
            action => Dispatcher.UIThread.Post(action),
            SessionRetry);
        MainViewModel model = new(localizer, host.Power, host.Prompt, link.Answer, host.SignOut, session: true);
        MainWindow window = new(model, fullScreen: true);

        _ = Task.Run(() => link.ServeAsync(model, stop.Token));
        desktop.Exit += (_, _) => stop.Cancel();

        return window;
    }
}

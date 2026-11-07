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

// Opens the window and feeds it the agent's messages. In DDT's session the window opens right away, because nothing
// else is on the screen. The console reconnects each time the agent's service restarts.
public sealed class ConsoleStartup
{
    // In DDT's session: the pause between two tries to reach the agent, and how long each try waits for the pipe.
    public static readonly TimeSpan SessionRetry = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan SessionConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly Func<Localizer, IClassicDesktopStyleApplicationLifetime, MainWindow> _open;

    public ConsoleStartup(IAgentConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        _open = (localizer, desktop) => OpenConnected(localizer, desktop, connection);
    }

    private ConsoleStartup(string sessionPipe) => _open = (localizer, desktop) => OpenSession(localizer, desktop, sessionPipe);

    public static ConsoleStartup ForSession(string pipeName) => new(pipeName);

    public Window Open(App app, IClassicDesktopStyleApplicationLifetime desktop)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(desktop);

        return _open(Localizer.Embedded(Catalogs.FromWindows()), desktop);
    }

    private static MainWindow OpenConnected(Localizer localizer, IClassicDesktopStyleApplicationLifetime desktop, IAgentConnection connection)
    {
        AgentLink link = new(connection);
        Inbox inbox = new(action => Dispatcher.UIThread.Post(action));
        WindowsPEPower power = new();

        MainViewModel model = new(
            localizer,
            power,
            new CommandPrompt(),
            (id, answer) => _ = link.AnswerAsync(id, answer),
            () => desktop.Shutdown(Program.Closed));

        MainWindow window = new(model, fullScreen: power.IsWindowsPE);
        inbox.Deliver(model.Receive, model.Ended);

        _ = Task.Run(async () => inbox.End(await link.ReadAsync(inbox.Add).ConfigureAwait(false)));

        desktop.Exit += (_, _) => link.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(1));

        return window;
    }

    // The agent asks here too, like a Pause step does. The link sends each answer to the connection that asked. F9 signs
    // out once the run is over.
    private static MainWindow OpenSession(Localizer localizer, IClassicDesktopStyleApplicationLifetime desktop, string pipeName)
    {
        string program = $"DDT console {ConsoleBuild.Version}";
        CancellationTokenSource stop = new();
        SessionLink link = new(
            async cancellation => await ClientConnection.ConnectToSessionAgentAsync(pipeName, program, SessionConnectTimeout, cancellation)
                .ConfigureAwait(false),
            action => Dispatcher.UIThread.Post(action),
            SessionRetry);
        MainViewModel model = new(localizer, new WindowsPEPower(), new CommandPrompt(), link.Answer, SessionSignOut.SignOut, session: true);
        MainWindow window = new(model, fullScreen: true);

        _ = Task.Run(() => link.ServeAsync(model, stop.Token));
        desktop.Exit += (_, _) => stop.Cancel();

        return window;
    }
}

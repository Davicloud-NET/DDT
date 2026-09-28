// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Agent;
using DDT.MachineConsole.Machine;
using DDT.MachineConsole.Texts;
using DDT.MachineConsole.ViewModels;
using DDT.MachineConsole.Views;

namespace DDT.MachineConsole;

// Opens the window and feeds it the agent's messages. In DDT's session the window opens at once, as nothing else is on
// the screen, and the console reconnects each time the agent's service restarts.
public sealed class ConsoleStartup
{
    // Between two attempts to reach the agent in DDT's session, and how long each may wait for the pipe.
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

    // Nothing is asked in the installed Windows, so no answer goes back; F9 signs out once the run is over.
    private static MainWindow OpenSession(Localizer localizer, IClassicDesktopStyleApplicationLifetime desktop, string pipeName)
    {
        CancellationTokenSource stop = new();
        MainViewModel model = new(localizer, new WindowsPEPower(), new CommandPrompt(), (_, _) => { }, SessionSignOut.SignOut, session: true);
        MainWindow window = new(model, fullScreen: true);

        _ = Task.Run(() => ServeSessionAsync(model, pipeName, stop.Token));
        desktop.Exit += (_, _) => stop.Cancel();

        return window;
    }

    private static async Task ServeSessionAsync(MainViewModel model, string pipeName, CancellationToken stop)
    {
        string program = $"DDT console {ConsoleBuild.Version}";

        try
        {
            while (!stop.IsCancellationRequested)
            {
                ClientConnection connection;

                try
                {
                    connection = await ClientConnection.ConnectToSessionAgentAsync(pipeName, program, SessionConnectTimeout, stop)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException or ConsoleProtocolException)
                {
                    await Task.Delay(SessionRetry, stop).ConfigureAwait(false);

                    continue;
                }

                AgentLink link = new(connection);

                await using (link.ConfigureAwait(false))
                {
                    // One inbox per connection: the agent sends its state and newest lines anew each time.
                    Inbox inbox = new(action => Dispatcher.UIThread.Post(action));
                    Dispatcher.UIThread.Post(() =>
                    {
                        model.Attached();
                        inbox.Deliver(model.Receive, _ => model.Detached());
                    });
                    inbox.End(await link.ReadAsync(inbox.Add).ConfigureAwait(false));
                }

                await Task.Delay(SessionRetry, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }
}

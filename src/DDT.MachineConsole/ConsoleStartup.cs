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

// The console once it is connected: the language Windows PE speaks, the window, and the pipe read from the moment the
// window exists until the agent closes it.
public sealed class ConsoleStartup(IAgentConnection connection)
{
    public Window Open(App app, IClassicDesktopStyleApplicationLifetime desktop)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(desktop);

        AgentLink link = new(connection);
        Inbox inbox = new(action => Dispatcher.UIThread.Post(action));
        WindowsPEPower power = new();
        Localizer localizer = Localizer.Embedded(Catalogs.FromWindows());

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
}

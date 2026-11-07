// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Agent;
using DDT.MachineConsole.ViewModels;

namespace DDT.MachineConsole;

// The console's link to the agent in DDT's session. It connects again each time the agent's service restarts, and an
// answer only goes to the connection that asked. post runs an action on the UI thread.
public sealed class SessionLink(Func<CancellationToken, Task<IAgentConnection>> connect, Action<Action> post, TimeSpan retry)
{
    // Both belong to the UI thread: the connection on the screen, and the questions it asked.
    private readonly HashSet<int> _asked = [];
    private AgentLink? _shown;

    // Called on the UI thread. A restarted agent numbers its questions from 1 again, so an answer never waits for the
    // next connection. Nothing is sent while the agent is away, or for a question this connection didn't ask.
    public void Answer(int questionId, ConsoleAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (_shown is { } link && _asked.Contains(questionId))
        {
            _ = link.AnswerAsync(questionId, answer);
        }
    }

    // Feeds the model on the UI thread, one connection after the other, until stop.
    public async Task ServeAsync(MainViewModel model, CancellationToken stop)
    {
        ArgumentNullException.ThrowIfNull(model);

        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (await ConnectAsync(stop).ConfigureAwait(false) is { } connection)
                {
                    await ReadAsync(connection, model).ConfigureAwait(false);
                }

                await Task.Delay(retry, stop).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    // Null while there's no agent to talk to.
    private async Task<IAgentConnection?> ConnectAsync(CancellationToken stop)
    {
        try
        {
            return await connect(stop).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException or ConsoleProtocolException)
        {
            return null;
        }
    }

    // One inbox per connection. The agent sends its state and newest lines again on every connection.
    private async Task ReadAsync(IAgentConnection connection, MainViewModel model)
    {
        AgentLink link = new(connection);

        await using (link.ConfigureAwait(false))
        {
            Inbox inbox = new(post);
            post(() => Show(link, inbox, model));
            inbox.End(await link.ReadAsync(inbox.Add).ConfigureAwait(false));
        }
    }

    private void Show(AgentLink link, Inbox inbox, MainViewModel model)
    {
        _shown = link;
        _asked.Clear();
        model.Attached();
        inbox.Deliver(message => Receive(link, message, model), _ => Gone(link, model));
    }

    // A busy UI thread may get to a connection only after the next one is on the screen. Then it shows nothing.
    private void Receive(AgentLink link, ConsoleMessage message, MainViewModel model)
    {
        if (_shown != link)
        {
            return;
        }

        if (message is QuestionMessage question)
        {
            _asked.Add(question.Id);
        }

        model.Receive(message);
    }

    private void Gone(AgentLink link, MainViewModel model)
    {
        if (_shown != link)
        {
            return;
        }

        _shown = null;
        model.Detached();
    }
}

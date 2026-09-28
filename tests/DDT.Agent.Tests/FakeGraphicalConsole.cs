// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.Agent.Tests;

// A stand-in for ddt-console.exe in the test's process. It connects to the agent's pipe with the given hello, keeps
// every message the agent sends, and answers each question with what answer returns. A null answer leaves the question
// open. CrashAt ends it silently, like a crash, at the first message it returns true for.
internal sealed class FakeGraphicalConsole(Func<ConsoleQuestion, ConsoleAnswer?>? answer = null)
{
    // What a crashed .NET process ends with.
    public const int CrashExitCode = unchecked((int)0xE0434352);

    private readonly Lock _lock = new();
    private readonly List<ConsoleMessage> _received = [];

    public HelloMessage Hello { get; init; } = new(HelloMessage.CurrentVersion, "Fake console");

    public Func<ConsoleMessage, bool> CrashAt { get; init; } = _ => false;

    // Why the agent refused it, if it did.
    public string? Refusal { get; private set; }

    public List<ConsoleMessage> Received
    {
        get
        {
            lock (_lock)
            {
                return [.. _received];
            }
        }
    }

    // Returns the exit code once the agent closed the pipe or the console crashed.
    public async Task<int> RunAsync(string pipeName, CancellationToken killed)
    {
        ConsoleClient client;

        try
        {
            client = await ConsoleClient.ConnectAsync(pipeName, Hello, TimeSpan.FromSeconds(10), killed);
        }
        catch (ConsoleProtocolException exception)
        {
            Refusal = exception.Message;

            return 2;
        }

        await using (client)
        {
            while (await client.ReceiveAsync(killed) is { } message)
            {
                lock (_lock)
                {
                    _received.Add(message);
                }

                if (CrashAt(message))
                {
                    return CrashExitCode;
                }

                if (message is QuestionMessage question && answer?.Invoke(question.Question) is { } answered)
                {
                    await client.AnswerAsync(question.Id, answered, killed);
                }
            }
        }

        return 0;
    }
}

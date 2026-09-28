// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.Agent.Consoles;

// Reads a console's answers into the question slot until the pipe closes.
internal static class ConsoleAnswers
{
    // Why it stopped. An answer to a question no longer open is left to the slot, which ignores it.
    public static async Task<string?> ReadAsync(ConsoleChannel channel, QuestionSlot questions, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                switch (await channel.ReceiveAsync(cancellationToken).ConfigureAwait(false))
                {
                    case null:
                        return "closed its pipe";
                    case AnswerMessage answer:
                        questions.Answer(answer.Id, answer.Answer);
                        break;
                    default:
                        return "sent a message only the agent sends";
                }
            }
        }
        catch (ConsoleProtocolException exception)
        {
            return ConsoleFailure.NotAMessage(exception);
        }
        catch (IOException exception)
        {
            return ConsoleFailure.ClosedPipe(exception);
        }
    }
}

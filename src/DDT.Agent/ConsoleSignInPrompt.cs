// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Agent;

public sealed class ConsoleSignInPrompt(AgentLog log, TimeProvider timeProvider, string? keyboardLayout) : ISignInPrompt
{
    // Console.ReadKey cannot be cancelled, so keys are polled instead: an approval on the web has to be able to
    // take the prompt away.
    private static readonly TimeSpan s_keyPollInterval = TimeSpan.FromMilliseconds(50);

    private bool _layoutShown;

    public bool IsAvailable => !Console.IsInputRedirected;

    public async Task<string?> ReadLineAsync(string label, bool secret, CancellationToken cancellationToken)
    {
        // A wrong layout turns a correct password into a lockout with nothing on screen to explain it.
        if (!_layoutShown && keyboardLayout is not null)
        {
            _layoutShown = true;
            log.Information($"Keyboard layout: {keyboardLayout}.");
        }

        DiscardTypedKeys();

        StringBuilder typed = new();
        log.HoldConsole($"{label}: ");

        try
        {
            while (true)
            {
                if (!Console.KeyAvailable)
                {
                    await Task.Delay(s_keyPollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                ConsoleKeyInfo key = Console.ReadKey(intercept: true);

                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();

                    return typed.ToString();
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (typed.Length > 0)
                    {
                        typed.Length--;
                        Console.Write("\b \b");
                    }

                    continue;
                }

                // AltGr arrives as Control plus Alt, and it types the @ and \ of user names on many layouts,
                // so characters are judged by what they are, not by the modifiers held.
                if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
                {
                    typed.Append(key.KeyChar);
                    Console.Write(secret ? '*' : key.KeyChar);
                }
            }
        }
        catch (OperationCanceledException)
        {
            DiscardTypedKeys();
            Console.WriteLine();

            return null;
        }
        finally
        {
            log.ReleaseConsole();
        }
    }

    // Keys typed while nothing reads them would land in the next field, or in the command shell once the agent
    // exits: a password in plain sight either way.
    private static void DiscardTypedKeys()
    {
        while (Console.KeyAvailable)
        {
            Console.ReadKey(intercept: true);
        }
    }
}

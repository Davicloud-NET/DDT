// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Consoles;

namespace DDT.Agent;

// Asks at the machine whether to start over in the boot environment instead of the restart into the installed Linux.
// That ends a run, so a key only stops the countdown, and a typed word confirms, as ERASE does for a disk.
public sealed class StartOverQuestion(IKeyWait keys, ISignInPrompt prompt, AgentLog log, TimeProvider timeProvider)
{
    public const string Word = "END";

    public const string NotConfirmed = $"{Word} was not typed, so the run goes on and the machine restarts into the installed Linux.";

    // How long the question waits for its answer. Nobody may be at the machine any more.
    public static readonly TimeSpan AnswerTime = TimeSpan.FromMinutes(1);

    public static string Label { get; } = $"Type {Word} to end the run and start over here";

    public static string NoAnswer { get; } = string.Create(
        CultureInfo.InvariantCulture,
        $"Nobody answered within {AnswerTime.TotalSeconds:0} seconds, so the run goes on and the machine restarts into the installed Linux.");

    // What the countdown says of the key, for the line that announces the restart.
    public static string Offer { get; } = "To start over here instead, press a key within that time. The agent then asks before it ends the run.";

    // Whether anyone can answer here. Without it the countdown runs and nothing is offered.
    public bool CanAsk => keys.CanPress && prompt.IsAvailable;

    public static string Asked(bool taken) =>
        "A key was pressed, so the restart waits. Starting over here ends the run" +
        (taken ? " that goes on in the installed Linux" : ", which the installed Linux has not taken over") +
        $": the machine registers as after any start and has to be authorized again. Type {Word} to do that. Any other answer, or none " +
        string.Create(CultureInfo.InvariantCulture, $"within {AnswerTime.TotalSeconds:0} seconds, restarts the machine into the installed Linux.");

    // True only when a key stopped the countdown and the word was typed. taken says whether the installed Linux has
    // the run. Throws OperationCanceledException once cancelled.
    public async Task<bool> ConfirmedWithinAsync(TimeSpan countdown, bool taken, CancellationToken cancellationToken)
    {
        if (!await keys.PressedWithinAsync(countdown, cancellationToken).ConfigureAwait(false) || !CanAsk)
        {
            return false;
        }

        log.Warning(Asked(taken));

        using CancellationTokenSource time = new(AnswerTime, timeProvider);
        using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, time.Token);
        string? typed = await prompt.ReadLineAsync(Label, secret: false, waiting.Token).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(typed?.Trim(), Word, StringComparison.Ordinal))
        {
            return true;
        }

        log.Information(typed is null ? NoAnswer : NotConfirmed);

        return false;
    }
}

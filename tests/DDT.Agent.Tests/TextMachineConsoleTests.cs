// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.ConsoleProtocol;
using Xunit;

namespace DDT.Agent.Tests;

// The text console asks a sequence's inputs one prompt at a time, and a Pause step with Enter, as Windows PE shows them
// when the graphical console is not there. What is typed never reaches the log.
public sealed class TextMachineConsoleTests : IDisposable
{
    private const string Password = "Tr0ub4dor&3-join";

    private static readonly ConsoleInput s_owner = new("Owner", "Owner", "Who gets the PC.", ConsoleInputKind.Text, [], null, true, 20, null);

    private static readonly ConsoleInput s_office = new(
        "Office",
        "Office",
        null,
        ConsoleInputKind.Choice,
        [new ConsoleChoice("Standard", null), new ConsoleChoice("ProPlus", "Professional Plus")],
        "Standard",
        true,
        null,
        null);

    private static readonly ConsoleInput s_languages = new(
        "Languages",
        "Languages",
        null,
        ConsoleInputKind.MultiChoice,
        [new ConsoleChoice("de-DE", "German"), new ConsoleChoice("fr-FR", "French"), new ConsoleChoice("it-IT", "Italian")],
        null,
        false,
        null,
        null);

    private static readonly ConsoleInput s_encrypt = new("Encrypt", "Encrypt the disk", null, ConsoleInputKind.YesNo, [], "true", true, null, null);

    private static readonly ConsoleInput s_join = new("Join", "Join account", null, ConsoleInputKind.Account, [], null, true, null, null, "corp.example");

    private readonly StringWriter _console = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _console.Dispose();

    [Fact]
    public async Task AsksEachInputAtItsOwnPromptAndNeverLogsTheAnswers()
    {
        ScriptedSignInPrompt prompt = new("Anna Berger", "2", "3, 1", "n", @"CORP\join", Password);

        ConsoleAnswer? answer = await Text(prompt).AskAsync(Inputs(s_owner, s_office, s_languages, s_encrypt, s_join), Cancellation);

        Assert.Equal(
            [
                new ConsoleInputValue("Owner", "Anna Berger"),
                new ConsoleInputValue("Office", "ProPlus"),
                new ConsoleInputValue("Languages", "de-DE;it-IT"),
                new ConsoleInputValue("Encrypt", "false"),
                new ConsoleInputValue("Join", null, @"CORP\join", Password),
            ],
            answer?.Values);
        Assert.Equal(
            ["Owner", "Number [Enter keeps the default]", "Numbers, separated by commas", "Encrypt the disk (y/n) [y]", "User name", @"Password for CORP\join (hidden)"],
            prompt.Labels);

        string log = _console.ToString();
        Assert.Contains("Install Windows asks these before it starts.", log, StringComparison.Ordinal);
        Assert.Contains("Owner: Who gets the PC.", log, StringComparison.Ordinal);
        Assert.Contains("  2. Professional Plus", log, StringComparison.Ordinal);
        Assert.Contains("  1. Standard (default)", log, StringComparison.Ordinal);
        Assert.Contains("For corp.example. DDT keeps the password for this run only and never shows it.", log, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, log, StringComparison.Ordinal);
        Assert.DoesNotContain("Anna Berger", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsTheDefaultsOnEnterAndLeavesWhatMayStayEmpty()
    {
        ScriptedSignInPrompt prompt = new(string.Empty, string.Empty, string.Empty);

        ConsoleAnswer? answer = await Text(prompt).AskAsync(Inputs(s_office, s_languages, s_encrypt), Cancellation);

        Assert.Equal(
            [new ConsoleInputValue("Office", "Standard"), new ConsoleInputValue("Languages", string.Empty), new ConsoleInputValue("Encrypt", "true")],
            answer?.Values);
    }

    [Fact]
    public async Task AsksAgainUntilAnAnswerFitsAndSaysWhy()
    {
        ScriptedSignInPrompt prompt = new(string.Empty, "A name longer than twenty characters", "Anna", "7", "zwei", "1", "maybe", "y");

        ConsoleAnswer? answer = await Text(prompt).AskAsync(
            Inputs(s_owner, s_office with { Default = null }, s_encrypt with { Default = null }),
            Cancellation);

        Assert.Equal(
            [new ConsoleInputValue("Owner", "Anna"), new ConsoleInputValue("Office", "Standard"), new ConsoleInputValue("Encrypt", "true")],
            answer?.Values);

        string log = _console.ToString();
        Assert.Contains("WARN  Owner needs an answer.", log, StringComparison.Ordinal);
        Assert.Contains("WARN  Owner takes at most 20 characters.", log, StringComparison.Ordinal);
        Assert.Equal(2, Count(log, "WARN  Type a number from 1 to 2."));
        Assert.Contains("WARN  Type y or n.", log, StringComparison.Ordinal);
    }

    // As at the sign-in, an empty password goes back to the user name.
    [Fact]
    public async Task GoesBackToTheUserNameOnAnEmptyPassword()
    {
        ScriptedSignInPrompt prompt = new("anna", string.Empty, @"CORP\join", Password);

        ConsoleAnswer? answer = await Text(prompt).AskAsync(Inputs(s_join), Cancellation);

        Assert.Equal([new ConsoleInputValue("Join", null, @"CORP\join", Password)], answer?.Values);
        Assert.Equal(["User name", "Password for anna (hidden)", "User name", @"Password for CORP\join (hidden)"], prompt.Labels);
        Assert.DoesNotContain(Password, _console.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShowsTheAgentsWordsAboutARefusedAnswerBeforeItsField()
    {
        ScriptedSignInPrompt prompt = new("Anna");

        await Text(prompt).AskAsync(
            new InputsQuestion("Install Windows", [s_owner with { Error = "The server knows no owner of that name." }], "The server did not take the answers."),
            Cancellation);

        string[] lines = _console.ToString().Split(Environment.NewLine);
        Assert.Contains(lines, line => line.EndsWith("WARN  The server did not take the answers.", StringComparison.Ordinal));
        Assert.True(
            Array.FindIndex(lines, line => line.EndsWith("Owner: Who gets the PC.", StringComparison.Ordinal))
            < Array.FindIndex(lines, line => line.EndsWith("WARN  The server knows no owner of that name.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ContinuesAPauseOnEnter()
    {
        ScriptedSignInPrompt prompt = new(string.Empty);

        ConsoleAnswer? answer = await Text(prompt).AskAsync(
            new PauseQuestion("Check the BIOS", "Set the boot order to the network first.\nThen come back here."),
            Cancellation);

        Assert.Equal(new ConsoleAnswer(Continue: true), answer);
        Assert.Equal(["Press Enter to go on"], prompt.Labels);

        string log = _console.ToString();
        Assert.Contains("WARN  Check the BIOS pauses the run.", log, StringComparison.Ordinal);
        Assert.Contains("INFO  Set the boot order to the network first.", log, StringComparison.Ordinal);
        Assert.Contains("INFO  Then come back here.", log, StringComparison.Ordinal);
    }

    // Continued on the web meanwhile: the agent withdraws the prompt, and nothing is answered.
    [Fact]
    public async Task GivesUpAPauseTheAgentWithdraws()
    {
        ScriptedSignInPrompt prompt = new();
        using CancellationTokenSource withdrawn = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);

        Task<ConsoleAnswer?> asked = Text(prompt).AskAsync(new PauseQuestion("Check the BIOS", "Set the boot order."), withdrawn.Token);
        await withdrawn.CancelAsync();

        Assert.Null(await asked);
        Assert.Equal(1, prompt.Cancelled);
    }

    private TextMachineConsole Text(ScriptedSignInPrompt prompt) => new(prompt, new AgentLog(new ImmediateTimeProvider(), _console));

    private static InputsQuestion Inputs(params ConsoleInput[] inputs) => new("Install Windows", inputs, null);

    private static int Count(string text, string part) => text.Split(part).Length - 1;
}

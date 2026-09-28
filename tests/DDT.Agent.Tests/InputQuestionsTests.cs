// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class InputQuestionsTests
{
    private static readonly AgentInput s_office = new(
        "Office",
        "Office",
        "Where the PC goes.",
        InputKind.Choice,
        [new InputChoice("VIE", "Vienna"), new InputChoice("GRZ", "Graz")],
        "VIE",
        true,
        null);

    private static readonly AgentInput s_tools = new("Tools", "Tools", null, InputKind.MultiChoice, [new InputChoice("Git"), new InputChoice("Node")], null, false, null);

    private static readonly AgentInput s_owner = new("Owner", "Owner", null, InputKind.Text, [], null, false, 8);

    private static readonly AgentInput s_encrypt = new("Encrypt", "Encrypt the disk", null, InputKind.YesNo, [], "true", true, null);

    private static readonly AgentInput s_join = new("JoinAccount", "Join account", null, InputKind.Account, [], null, true, null);

    private static readonly AgentInput[] s_inputs = [s_office, s_tools, s_owner, s_encrypt, s_join];

    [Fact]
    public void AsksAnInputAsTheConsoleShowsIt()
    {
        SequenceDefinition definition = new(SequenceDefinition.CurrentVersion, [TestRuns.Script(1)])
        {
            Inputs = [new InputDeclaration { Name = "JoinAccount", Label = "Join account", Kind = InputKind.Account, Account = new AccountDestination { Domain = "corp.example" } }],
        };

        ConsoleInput office = InputQuestions.ToConsole(s_office, "Choose one.");

        Assert.Equal(new ConsoleInput("Office", "Office", "Where the PC goes.", ConsoleInputKind.Choice, office.Choices, "VIE", true, null, "Choose one."), office);
        Assert.Equal([new ConsoleChoice("VIE", "Vienna"), new ConsoleChoice("GRZ", "Graz")], office.Choices);
        Assert.Equal("corp.example", InputQuestions.ToConsole(s_join, null, definition).Domain);
        Assert.Equal("ad.example", InputQuestions.ToConsole(s_join with { Domain = "ad.example" }, null, definition).Domain);
        Assert.Null(InputQuestions.ToConsole(s_office, null, definition).Domain);
    }

    // What the console can get wrong, field by field, in words that name the input and never what was typed.
    [Fact]
    public void ChecksEachAnswer()
    {
        IReadOnlyDictionary<string, string> problems = InputQuestions.Check(
            s_inputs,
            [
                new ConsoleInputValue("office", "LNZ"),
                new ConsoleInputValue("Tools", "Git;Rust"),
                new ConsoleInputValue("Owner", "Somebody Else"),
                new ConsoleInputValue("Encrypt", "maybe"),
                new ConsoleInputValue("JoinAccount", null, @"CORP\join", ""),
            ]);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Office"] = "Choose one of the choices shown for Office.",
                ["Tools"] = "Choose only choices shown for Tools.",
                ["Owner"] = "Owner takes at most 8 characters.",
                ["Encrypt"] = "Answer Encrypt the disk with yes or no.",
                ["JoinAccount"] = "Join account needs a user name and a password.",
            },
            problems);
        Assert.Equal(["Office", "Encrypt", "JoinAccount"], InputQuestions.Check(s_inputs, []).Keys);
    }

    // One answer per input, trimmed, an Account input's as its user name and password alone.
    [Fact]
    public void AnswersEachInputAsTheServerTakesIt()
    {
        IReadOnlyList<InputAnswer> answers = InputQuestions.Answers(
            s_inputs,
            [
                new ConsoleInputValue("Office", " GRZ "),
                new ConsoleInputValue("Encrypt", "false"),
                new ConsoleInputValue("JoinAccount", "ignored", @" CORP\join ", "Pa55 word"),
                new ConsoleInputValue("Owner", "  "),
            ]);

        Assert.Equal(
            [
                new InputAnswer("Office", "GRZ"),
                new InputAnswer("Tools", null),
                new InputAnswer("Owner", null),
                new InputAnswer("Encrypt", "false"),
                new InputAnswer("JoinAccount", null, @"CORP\join", "Pa55 word"),
            ],
            answers);
    }

    [Fact]
    public void FindsTheInputsAServersRefusalNames()
    {
        Dictionary<string, string> errors = new()
        {
            ["answers.office"] = "Graz is closed.",
            ["answers[Owner]"] = "Too long.",
            ["computerName"] = "Taken.",
        };

        Assert.Equal(
            new Dictionary<string, string> { ["Office"] = "Graz is closed.", ["Owner"] = "Too long." },
            InputQuestions.FieldErrors(s_inputs, errors));
        Assert.Null(InputQuestions.FieldErrors(s_inputs, new Dictionary<string, string> { ["computerName"] = "Taken." }));
        Assert.Null(InputQuestions.FieldErrors(s_inputs, null));
    }
}

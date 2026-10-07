// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using DDT.ConsoleProtocol;
using Xunit;

namespace DDT.Agent.Tests;

// The messages between the agent and ddt-console.exe, in the form the console project reads and writes them.
public sealed class ConsoleProtocolTests
{
    private static readonly Guid s_stepId = Guid.Parse("0193a4b2-0000-7000-8000-00000000b001");

    private static readonly ConsoleDisk s_disk = new(2, "Test disk 2", 256L * 1024 * 1024 * 1024, "Nvme", 3);

    private static readonly ConsoleState s_state = new(
        ConsoleStage.Running,
        "1.4.0",
        false,
        new ConsoleServer("https://ddt.example:8443/", "the server did not answer within 30 s", ConnectionStage.Answer, 2),
        new ConsoleMachine(
            "Contoso",
            "Laptop 7",
            "SN-1",
            "4c4c4544-0000-1010-8000-b2c04f4c4d32",
            ["00155D012A0B", "00155D012A0C"],
            ["10.0.0.23"],
            true,
            MicrosoftUefiCas.Ca2011 | MicrosoftUefiCas.Ca2023,
            [s_disk],
            "German (Germany)"),
        Guid.Parse("0193a4b2-0000-7000-8000-000000000001"),
        null,
        new ConsoleRun(
            Guid.Parse("0193a4b2-0000-7000-8000-0000000000f1"),
            "Install Windows",
            [new ConsoleStep(s_stepId, "Apply image", "applyImage", ConsolePhase.WindowsPE, ConsoleStepState.Running, null)],
            s_stepId,
            42,
            ConsoleActivity.Step),
        new ConsoleRestart(RestartReason.StepAsked, RestartTarget.WindowsPE),
        new ConsoleProblem("The run failed: no disk.", ConsoleRemedy.RunAgain));

    // A repeat holding an IF that took Else on its second pass, so the script in Then was skipped.
    private static readonly Guid s_repeatId = Guid.Parse("0193a4b2-0000-7000-8000-00000000b002");
    private static readonly Guid s_ifId = Guid.Parse("0193a4b2-0000-7000-8000-00000000b003");

    private static readonly ConsoleStep[] s_tree =
    [
        new(s_repeatId, "Until the dock answers", "repeat", ConsolePhase.WindowsPE, ConsoleStepState.Running, null, Pass: 1, Iteration: 2),
        new(s_ifId, "If a ThinkPad", "if", ConsolePhase.WindowsPE, ConsoleStepState.Done, null, s_repeatId, 1, 2, Branch: ConsoleBranch.Else),
        new(s_stepId, "Dock firmware", "runScript", ConsolePhase.WindowsPE, ConsoleStepState.Skipped, null, s_ifId, 2, 2),
        new(Guid.NewGuid(), "Check the BIOS", "pause", ConsolePhase.WindowsPE, ConsoleStepState.Running, null, s_repeatId, 1, 2),
    ];

    private static readonly ConsoleInput[] s_inputs =
    [
        new("Owner", "Owner", "Who gets the PC.", ConsoleInputKind.Text, [], null, true, 64, "An owner is needed."),
        new("Office", "Office", null, ConsoleInputKind.Choice, [new ConsoleChoice("Standard", null), new ConsoleChoice("ProPlus", "Professional Plus")], "Standard", false, null, null),
        new("Languages", "Languages", null, ConsoleInputKind.MultiChoice, [new ConsoleChoice("de-DE", "German")], "de-DE", false, null, null),
        new("Encrypt", "Encrypt the disk", null, ConsoleInputKind.YesNo, [], "true", false, null, null),
        new("JoinAccount", "Join account", null, ConsoleInputKind.Account, [], null, true, null, null, "corp.example"),
    ];

    public static TheoryData<string> Messages => [.. s_messages.Keys];

    private static readonly Dictionary<string, ConsoleMessage> s_messages = new()
    {
        ["hello"] = new HelloMessage(HelloMessage.CurrentVersion, "DDT agent 1.4.0"),
        ["refused"] = new RefusedMessage(1, "This agent speaks version 1 of the console protocol, not 2."),
        ["state"] = new StateMessage(s_state),
        ["log"] = new LogMessage([new ConsoleLogLine(new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero), ConsoleLogLevel.Warning, "Wrong user name or password.", s_stepId)]),
        ["sign-in"] = new QuestionMessage(1, new SignInQuestion(SignInField.Password, "bob", "Wrong user name or password.")),
        ["sequence"] = new QuestionMessage(2, new SequenceQuestion([new SequenceOption(Guid.NewGuid(), "Install Windows", "Office", true, true, false, 30_000, false, false)])),
        ["disk"] = new QuestionMessage(3, new DiskQuestion("Install Windows", [s_disk])),
        ["computer name"] = new QuestionMessage(4, new ComputerNameQuestion("Install Windows", 15, "A computer name holds at most 15 characters.", "PC-00042")),
        ["erase"] = new QuestionMessage(5, new EraseQuestion("Install Windows", s_disk, "ERASE")),
        ["secure boot"] = new QuestionMessage(6, new SecureBootQuestion("Install Linux", "noble", SecureBootProblem.UntrustedCa, MicrosoftUefiCas.Ca2023, "ANYWAY")),
        ["withdraw"] = new WithdrawMessage(6),
        ["answer"] = new AnswerMessage(6, new ConsoleAnswer(Text: "ANYWAY", SequenceId: Guid.NewGuid(), DiskNumber: 2, Back: true)),
        ["tree"] = new StateMessage(s_state with { Run = s_state.Run! with { Steps = s_tree, Activity = ConsoleActivity.Paused } }),
        ["inputs"] = new QuestionMessage(7, new InputsQuestion("Install Windows", s_inputs, "The server did not take the answers.")),
        ["pause"] = new QuestionMessage(8, new PauseQuestion("Check the BIOS", "Check the BIOS of PC-0042, then continue.")),
        ["answer with values"] = new AnswerMessage(7, new ConsoleAnswer(Values: [new ConsoleInputValue("Office", "ProPlus"), new ConsoleInputValue("JoinAccount", null, @"CORP\join", "Secret")])),
        ["continue"] = new AnswerMessage(8, new ConsoleAnswer(Continue: true)),
    };

    // Every kind of input is asked in words, and a password never ends up in text a log would show.
    [Fact]
    public void NamesTheInputsInWordsAndKeepsPasswordsOutOfTheText()
    {
        string json = Json(new QuestionMessage(7, new InputsQuestion("Install Windows", [s_inputs[^1]], null)));
        ConsoleInputValue answer = new("JoinAccount", null, @"CORP\join", "Secret-Join-Password");
        ConsoleInputKind[] kinds = [.. s_inputs.Select(input => input.Kind)];

        Assert.Equal(
            """{"type":"question","id":7,"question":{"kind":"inputs","sequenceName":"Install Windows","inputs":[{"name":"JoinAccount","label":"Join account","help":null,"kind":"Account","choices":[],"default":null,"required":true,"maxLength":null,"error":null,"domain":"corp.example"}],"error":null}}""",
            json);
        Assert.Equal(Enum.GetValues<ConsoleInputKind>(), kinds);
        Assert.DoesNotContain("Secret-Join-Password", answer.ToString(), StringComparison.Ordinal);
        Assert.Contains(@"CORP\join", answer.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Messages))]
    public async Task EveryMessageComesThroughAsItWasSent(string name)
    {
        ConsoleMessage sent = s_messages[name];
        using MemoryStream stream = new();
        using ConsoleChannel channel = new(stream);

        await channel.SendAsync(sent, TestContext.Current.CancellationToken);
        stream.Position = 0;
        ConsoleMessage? received = await channel.ReceiveAsync(TestContext.Current.CancellationToken);

        // Lists compare by reference in records, so the JSON says whether everything came through.
        Assert.NotNull(received);
        Assert.Equal(sent.GetType(), received.GetType());
        Assert.Equal(Json(sent), Json(received));
        Assert.Null(await channel.ReceiveAsync(TestContext.Current.CancellationToken));
    }

    // Every version of the agent and the console reads this, so it never changes.
    [Fact]
    public void TheHelloIsFrozen() =>
        Assert.Equal("""{"type":"hello","version":1,"program":"DDT agent 1.4.0"}""", Json(new HelloMessage(1, "DDT agent 1.4.0")));

    [Fact]
    public void NamesMessagesQuestionsAndValuesInWords()
    {
        string json = Json(new QuestionMessage(5, new SecureBootQuestion("Install Linux", "noble", SecureBootProblem.UntrustedCa, MicrosoftUefiCas.Ca2011 | MicrosoftUefiCas.Ca2023, "ANYWAY")));

        Assert.Equal(
            """{"type":"question","id":5,"question":{"kind":"secureBoot","sequenceName":"Install Linux","imageName":"noble","problem":"UntrustedCa","signedUnder":"Ca2011, Ca2023","word":"ANYWAY"}}""",
            json);
    }

    [Fact]
    public async Task ReadsMessagesOneAfterTheOther()
    {
        using MemoryStream stream = new();
        using ConsoleChannel channel = new(stream);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await channel.SendAsync(new WithdrawMessage(1), cancellationToken);
        await channel.SendAsync(new WithdrawMessage(2), cancellationToken);
        stream.Position = 0;

        Assert.Equal(new WithdrawMessage(1), await channel.ReceiveAsync(cancellationToken));
        Assert.Equal(new WithdrawMessage(2), await channel.ReceiveAsync(cancellationToken));
        Assert.Null(await channel.ReceiveAsync(cancellationToken));
    }

    [Fact]
    public async Task RefusesAStreamThatEndsInsideAMessage()
    {
        byte[] frame = Frame("""{"type":"withdraw","id":1}""");

        await Assert.ThrowsAsync<ConsoleProtocolException>(() => ReceiveAsync(frame[..^3]));
        await Assert.ThrowsAsync<ConsoleProtocolException>(() => ReceiveAsync(frame[..2]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ConsoleChannel.MaxMessageBytes + 1)]
    public async Task RefusesALengthNoMessageHas(int length)
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);

        await Assert.ThrowsAsync<ConsoleProtocolException>(() => ReceiveAsync(header));
    }

    [Theory]
    [InlineData("""{"type":"shutdown"}""")]
    [InlineData("""{"id":1}""")]
    [InlineData("""{"type":"answer","id":1,"answer":{"text":1}}""")]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task RefusesWhatIsNotAMessage(string json) =>
        await Assert.ThrowsAsync<ConsoleProtocolException>(() => ReceiveAsync(Frame(json)));

    [Fact]
    public void GivesEveryPipeANameOfItsOwn()
    {
        string first = ConsolePipe.NewName();

        Assert.Matches("^ddt-console-[0-9a-f]{32}$", first);
        Assert.NotEqual(first, ConsolePipe.NewName());
    }

    [Fact]
    public void FindsThePipeNameInTheConsolesCommandLine()
    {
        string name = ConsolePipe.NewName();

        Assert.Equal(name, ConsolePipe.NameFrom(["--pipe", name]));
        Assert.Equal(name, ConsolePipe.NameFrom(["--verbose", "--pipe", name, "--other"]));
        Assert.Null(ConsolePipe.NameFrom(["--pipe"]));
        Assert.Null(ConsolePipe.NameFrom(["--pipe", @"\\server\pipe\other"]));
        Assert.Null(ConsolePipe.NameFrom([]));
    }

    private static string Json(ConsoleMessage message) => JsonSerializer.Serialize(message, ConsoleProtocolJsonContext.Default.ConsoleMessage);

    private static byte[] Frame(string json)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);

        return frame;
    }

    private static async Task<ConsoleMessage?> ReceiveAsync(byte[] bytes)
    {
        using MemoryStream stream = new(bytes);
        using ConsoleChannel channel = new(stream);

        return await channel.ReceiveAsync(TestContext.Current.CancellationToken);
    }
}

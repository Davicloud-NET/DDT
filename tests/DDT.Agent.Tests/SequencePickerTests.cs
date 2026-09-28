// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Images;
using DDT.Contracts.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class SequencePickerTests
{
    private static readonly AgentSequenceChoice s_install = new(
        Guid.Parse("0193a4b2-0000-7000-8000-00000000e001"),
        "Install Windows 11",
        "Windows 11 Pro with the office drivers",
        ErasesDisk: true,
        NeedsComputerName: false,
        RequiredBytes: 30L * 1024 * 1024 * 1024,
        Suggested: false);

    private static readonly AgentSequenceChoice s_withInputs = s_install with
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-00000000e003"),
        NeedsComputerName = true,
        Inputs =
        [
            new AgentInput("Office", "Office", null, InputKind.Choice, [new InputChoice("VIE", "Vienna"), new InputChoice("GRZ", "Graz")], "VIE", true, null),
            new AgentInput("JoinAccount", "Join account", null, InputKind.Account, [], null, true, null, Domain: "corp.example"),
        ],
    };

    private static readonly AgentSequenceChoice s_inventory = new(
        Guid.Parse("0193a4b2-0000-7000-8000-00000000e002"),
        "Inventory",
        null,
        ErasesDisk: false,
        NeedsComputerName: false,
        RequiredBytes: 0,
        Suggested: false);

    [Fact]
    public async Task PicksASequenceForTheOnlyDiskAfterErase()
    {
        (SequencePicker picker, ScriptedSignInPrompt prompt, _) = Create([s_inventory, s_install], [FakeDeploymentTools.Disk(0)], "2", "ERASE");

        AgentRunRequest? request = await AnswerAllAsync(picker);

        Assert.Equal(new AgentRunRequest(s_install.Id, 0, null), request);
        Assert.Equal(["Sequence number", "Type ERASE to continue"], prompt.Labels);
        Assert.Equal(FakeDeploymentTools.Disk(0), picker.ChosenDisk);
    }

    [Fact]
    public async Task StartsASequenceThatErasesNoDiskWithoutAskingForOne()
    {
        (SequencePicker picker, ScriptedSignInPrompt prompt, _) = Create(
            [s_install, s_inventory],
            [FakeDeploymentTools.Disk(0), FakeDeploymentTools.Disk(1)],
            "2");

        AgentRunRequest? request = await AnswerAllAsync(picker);

        Assert.Equal(new AgentRunRequest(s_inventory.Id, null, null), request);
        Assert.Equal(["Sequence number"], prompt.Labels);
        Assert.Null(picker.ChosenDisk);
    }

    [Fact]
    public async Task ListsTheSuggestedSequencesFirst()
    {
        AgentSequenceChoice suggested = s_inventory with { Suggested = true };
        (SequencePicker picker, _, StringWriter console) = Create([s_install, suggested], [FakeDeploymentTools.Disk(0)], "1");

        AgentRunRequest? request = await AnswerAllAsync(picker);

        Assert.Equal(suggested.Id, request?.SequenceId);
        Assert.Contains(Lines(console), line => line.EndsWith("  1. Inventory (suggested for this machine)", StringComparison.Ordinal));
        Assert.Contains(
            Lines(console),
            line => line.EndsWith("  2. Install Windows 11 (erases a disk, needs 30 GB): Windows 11 Pro with the office drivers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AsksAgainForANumberThatIsNotOnTheList()
    {
        (SequencePicker picker, ScriptedSignInPrompt prompt, StringWriter console) = Create(
            [s_install, s_inventory],
            [FakeDeploymentTools.Disk(0)],
            "3",
            "one",
            " 1 ",
            "ERASE");

        AgentRunRequest? request = await AnswerAllAsync(picker);

        Assert.Equal(s_install.Id, request?.SequenceId);
        Assert.Equal(["Sequence number", "Sequence number", "Sequence number", "Type ERASE to continue"], prompt.Labels);
        Assert.Equal(2, Lines(console).Count(line => line.EndsWith("Type a number from 1 to 2.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AsksForTheDiskWhenThereAreSeveral()
    {
        (SequencePicker picker, ScriptedSignInPrompt prompt, StringWriter console) = Create(
            [s_install],
            [FakeDeploymentTools.Disk(0), FakeDeploymentTools.Disk(2, partitions: 3)],
            "1",
            "1",
            "2",
            "ERASE");

        AgentRunRequest? request = await AnswerAllAsync(picker);

        Assert.Equal(new AgentRunRequest(s_install.Id, 2, null), request);
        Assert.Equal(FakeDeploymentTools.Disk(2, partitions: 3), picker.ChosenDisk);
        Assert.Equal(["Sequence number", "Disk number", "Disk number", "Type ERASE to continue"], prompt.Labels);
        Assert.Contains(Lines(console), line => line.EndsWith("Type one of the disk numbers shown: 0, 2.", StringComparison.Ordinal));
        Assert.Contains(
            Lines(console),
            line => line.EndsWith("All data on disk 2 (Test disk 2, 256 GB, 3 partitions) will be erased by Install Windows 11.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AsksForAValidComputerNameOnlyWhenTheSequenceNeedsOne(bool erasesDisk)
    {
        AgentSequenceChoice named = s_inventory with { ErasesDisk = erasesDisk, NeedsComputerName = true };
        string[] typed = erasesDisk ? ["1", "PC_01", "PC-01", "ERASE"] : ["1", "PC_01", "PC-01"];
        (SequencePicker picker, ScriptedSignInPrompt prompt, StringWriter console) = Create([named], [FakeDeploymentTools.Disk(0)], typed);

        AgentRunRequest? request = await AnswerAllAsync(picker);

        Assert.Equal(new AgentRunRequest(named.Id, erasesDisk ? 0 : null, "PC-01"), request);
        Assert.Equal(
            erasesDisk
                ? ["Sequence number", "Computer name", "Computer name", "Type ERASE to continue"]
                : ["Sequence number", "Computer name", "Computer name"],
            prompt.Labels);
        Assert.Contains(Lines(console), line => line.EndsWith("A computer name can hold only the letters A to Z, digits and hyphens.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("erase")]
    [InlineData("yes")]
    public async Task AnythingButEraseGoesBackToTheList(string typed)
    {
        (SequencePicker picker, ScriptedSignInPrompt prompt, StringWriter console) = Create(
            [s_install, s_inventory],
            [FakeDeploymentTools.Disk(0)],
            "1",
            typed,
            "2");

        AgentRunRequest? request = await AnswerAllAsync(picker);

        Assert.Equal(s_inventory.Id, request?.SequenceId);
        Assert.Equal(["Sequence number", "Type ERASE to continue", "Sequence number"], prompt.Labels);
        Assert.Contains(Lines(console), line => line.EndsWith("Nothing was erased.", StringComparison.Ordinal));
        Assert.Equal(2, Lines(console).Count(line => line.EndsWith("Task sequences this machine can run:", StringComparison.Ordinal)));
    }

    private static readonly AgentSequenceChoice s_linux = new(
        Guid.Parse("0193a4b2-0000-7000-8000-00000000e003"),
        "Install Linux",
        null,
        ErasesDisk: true,
        NeedsComputerName: false,
        RequiredBytes: 4L * 1024 * 1024 * 1024,
        Suggested: false,
        RawImageName: "custom-image",
        RawImageBootCapability: ImageBootCapability.NotSigned);

    [Fact]
    public async Task AsksForAnywayBeforeWritingAnImageTheFirmwareWouldNotStart()
    {
        (SequencePicker picker, ScriptedSignInPrompt prompt, StringWriter console) = Create([s_linux], [FakeDeploymentTools.Disk(0)], true, "1", "ERASE", "ANYWAY");

        AgentRunRequest? request = await AnswerAllAsync(picker);

        Assert.Equal(new AgentRunRequest(s_linux.Id, 0, null, AllowSecureBootMismatch: true), request);
        Assert.Equal(["Sequence number", "Type ERASE to continue", "Type ANYWAY to write it all the same"], prompt.Labels);
        Assert.Contains(Lines(console), line => line.EndsWith("1. Install Linux (erases a disk, not for Secure Boot, needs 4 GB)", StringComparison.Ordinal));
        Assert.Contains(
            Lines(console),
            line => line.EndsWith("custom-image will not start with Secure Boot on, and this machine has Secure Boot on. It starts only once Secure Boot is turned off in the firmware setup, or your own key is enrolled.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GoesBackToTheListWithoutAnyway()
    {
        (SequencePicker picker, ScriptedSignInPrompt prompt, _) = Create([s_linux], [FakeDeploymentTools.Disk(0)], true, "1", "ERASE", "yes", "1", "ERASE", "ANYWAY");

        AgentRunRequest? request = await AnswerAllAsync(picker);

        Assert.True(request?.AllowSecureBootMismatch);
        Assert.Equal(6, prompt.Labels.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task AsksNoAnywayWhereSecureBootIsNotOn(bool? secureBootEnabled)
    {
        (SequencePicker picker, ScriptedSignInPrompt prompt, _) = Create([s_linux], [FakeDeploymentTools.Disk(0)], secureBootEnabled, "1", "ERASE");

        Assert.Equal(new AgentRunRequest(s_linux.Id, 0, null), await AnswerAllAsync(picker));
        Assert.Equal(["Sequence number", "Type ERASE to continue"], prompt.Labels);
    }

    [Fact]
    public async Task AsksForAnywayForASignedImageWhereTheFirmwareDoesNotTrustItsCa()
    {
        AgentSequenceChoice signed = s_linux with
        {
            RawImageBootCapability = ImageBootCapability.SecureBootOk,
            RawImageSignedUnder = UefiCa.Microsoft2023,
        };
        StringWriter console = new();
        ScriptedSignInPrompt prompt = new("1", "ERASE", "ANYWAY");
        SequencePicker picker = TextPicker(prompt, console);
        picker.Offer([signed], [FakeDeploymentTools.Disk(0)], secureBootEnabled: true, trustedUefiCas: UefiCa.Microsoft2011);

        Assert.Equal(new AgentRunRequest(signed.Id, 0, null, AllowSecureBootMismatch: true), await AnswerAllAsync(picker));
        Assert.Contains(Lines(console), line => line.EndsWith("1. Install Linux (erases a disk, not for this machine's Secure Boot, needs 4 GB)", StringComparison.Ordinal));
        Assert.Contains(
            Lines(console),
            line => line.EndsWith("custom-image is signed under Microsoft's third-party UEFI CA 2023, which this machine's firmware does not trust. It starts only once that CA is allowed or Secure Boot is turned off in the firmware setup.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AsksNoAnywayForAnImageSignedForSecureBoot()
    {
        AgentSequenceChoice signed = s_linux with { RawImageBootCapability = ImageBootCapability.SecureBootOk };
        (SequencePicker picker, ScriptedSignInPrompt prompt, _) = Create([signed], [FakeDeploymentTools.Disk(0)], true, "1", "ERASE");

        Assert.Equal(new AgentRunRequest(signed.Id, 0, null), await AnswerAllAsync(picker));
        Assert.Equal(2, prompt.Labels.Count);
    }

    [Fact]
    public async Task ARefusedChoiceStartsOverWithAFreshList()
    {
        (SequencePicker picker, _, _) = Create([s_install], [FakeDeploymentTools.Disk(0)], "1", "ERASE");

        Assert.NotNull(await AnswerAllAsync(picker));

        picker.Refused("This machine cannot pick a sequence now.");

        Assert.False(picker.IsOffered);
    }

    // The inputs asked at the machine come after the computer name, all on one page, and before ERASE; their answers go
    // with the choice.
    [Fact]
    public async Task AsksTheSequencesInputsAfterTheComputerNameAndSendsTheAnswers()
    {
        ScriptedMachineConsole console = new(
            ScriptedMachineConsole.Sequence("Install Windows 11"),
            ScriptedMachineConsole.Typed("PC-042"),
            _ => new ConsoleAnswer(Values: [new ConsoleInputValue("Office", "GRZ"), new ConsoleInputValue("JoinAccount", null, @"CORP\join", "Pa55-word")]),
            ScriptedMachineConsole.Typed("ERASE"));
        SequencePicker picker = new(console, new AgentLog(new ImmediateTimeProvider(), TextWriter.Null));
        picker.Offer([s_withInputs], [FakeDeploymentTools.Disk(0)]);

        AgentRunRequest? request = await AnswerAllAsync(picker);

        Assert.Equal(
            [typeof(SequenceQuestion), typeof(ComputerNameQuestion), typeof(InputsQuestion), typeof(EraseQuestion)],
            console.Questions.Select(question => question.GetType()));
        InputsQuestion inputs = (InputsQuestion)console.Questions[2];
        Assert.Equal(("Install Windows 11", null), (inputs.SequenceName, inputs.Error));
        Assert.Equal(["Office", "JoinAccount"], inputs.Inputs.Select(input => input.Name));
        Assert.Equal("corp.example", inputs.Inputs[1].Domain);
        Assert.Equal((s_withInputs.Id, 0, "PC-042"), (request?.SequenceId, request?.DiskNumber, request?.ComputerName));
        Assert.Equal([new InputAnswer("Office", "GRZ"), new InputAnswer("JoinAccount", null, @"CORP\join", "Pa55-word")], request?.Answers);
    }

    // What the console got wrong is asked again at once, and what the server did not take after the choice was sent;
    // the rest of the choice stands, and ERASE is asked again. What was typed never reaches the log.
    [Fact]
    public async Task AsksTheInputsAgainWithWhatWasWrong()
    {
        StringWriter lines = new();
        ScriptedMachineConsole console = new(
            ScriptedMachineConsole.Sequence("Install Windows 11"),
            ScriptedMachineConsole.Typed("PC-042"),
            _ => new ConsoleAnswer(Values: [new ConsoleInputValue("Office", "LNZ")]),
            _ => new ConsoleAnswer(Values: [new ConsoleInputValue("Office", "GRZ"), new ConsoleInputValue("JoinAccount", null, "join", "Pa55-word")]),
            ScriptedMachineConsole.Typed("ERASE"),
            _ => new ConsoleAnswer(Values: [new ConsoleInputValue("Office", "VIE"), new ConsoleInputValue("JoinAccount", null, "join", "Pa55-word")]),
            ScriptedMachineConsole.Typed("ERASE"));
        SequencePicker picker = new(console, new AgentLog(new ImmediateTimeProvider(), lines));
        picker.Offer([s_withInputs], [FakeDeploymentTools.Disk(0)]);

        Assert.NotNull(await AnswerAllAsync(picker));

        picker.Refused("Graz is closed.", new Dictionary<string, string> { ["answers.Office"] = "Graz is closed this week." });

        Assert.True(picker.IsOffered);
        Assert.Equal("VIE", (await AnswerAllAsync(picker))?.Answers?[0].Value);

        InputsQuestion[] asked = [.. console.Questions.OfType<InputsQuestion>()];
        Assert.Equal(3, asked.Length);
        Assert.Equal(
            ["Choose one of the choices shown for Office.", "Join account needs a user name and a password."],
            asked[1].Inputs.Select(input => input.Error));
        Assert.Equal(("Graz is closed.", "Graz is closed this week.", null), (asked[2].Error, asked[2].Inputs[0].Error, asked[2].Inputs[1].Error));
        Assert.Equal(2, console.Questions.Count(question => question is EraseQuestion));
        Assert.DoesNotContain("Pa55-word", lines.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("LNZ", lines.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OffersOnlySequencesThatEraseNoDiskWhenThereIsNone()
    {
        (SequencePicker picker, _, _) = Create([s_install], []);

        Assert.False(picker.IsOffered);

        picker.Offer([s_install, s_inventory], []);

        Assert.True(picker.IsOffered);

        picker.Offer([], [FakeDeploymentTools.Disk(0)]);

        Assert.False(picker.IsOffered);
    }

    private static (SequencePicker Picker, ScriptedSignInPrompt Prompt, StringWriter Console) Create(
        AgentSequenceChoice[] sequences,
        LocalDisk[] disks,
        params string[] typed) => Create(sequences, disks, null, typed);

    private static (SequencePicker Picker, ScriptedSignInPrompt Prompt, StringWriter Console) Create(
        AgentSequenceChoice[] sequences,
        LocalDisk[] disks,
        bool? secureBootEnabled,
        params string[] typed)
    {
        StringWriter console = new();
        ScriptedSignInPrompt prompt = new(typed);
        SequencePicker picker = TextPicker(prompt, console);
        picker.Offer(sequences, disks, secureBootEnabled);

        return (picker, prompt, console);
    }

    // Asks as the text console does, which these tests pin.
    private static SequencePicker TextPicker(ScriptedSignInPrompt prompt, StringWriter console)
    {
        AgentLog log = new(new ImmediateTimeProvider(), console);

        return new SequencePicker(new TextMachineConsole(prompt, log), log);
    }

    private static string[] Lines(StringWriter console) => console.ToString().Split(Environment.NewLine);

    // Reads and accepts typed lines until the picker has a request.
    private static async Task<AgentRunRequest?> AnswerAllAsync(SequencePicker picker)
    {
        while (await picker.ReadAsync(TestContext.Current.CancellationToken) is { } answer)
        {
            if (picker.Accept(answer) is { } request)
            {
                return request;
            }
        }

        return null;
    }
}

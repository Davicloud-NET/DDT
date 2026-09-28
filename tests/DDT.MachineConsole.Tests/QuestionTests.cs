// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.ViewModels;
using Xunit;

namespace DDT.MachineConsole.Tests;

// What each question sends: the field it names, and Back only where the question allows it.
public sealed class QuestionTests
{
    [Fact]
    public void SignsInOneFieldAtATime()
    {
        TestConsole console = Asked(1, new SignInQuestion(SignInField.UserName, null, null));
        SignInViewModel signIn = Assert.IsType<SignInViewModel>(console.Model.Question);

        Assert.False(signIn.SubmitCommand.CanExecute(null));
        Assert.False(signIn.CanGoBack);

        signIn.UserName = " anna ";
        signIn.SubmitCommand.Execute(null);
        console.Ask(2, new SignInQuestion(SignInField.Password, "anna", null));
        signIn.Password = "correct horse";
        signIn.SubmitCommand.Execute(null);
        console.Ask(3, new SignInQuestion(SignInField.Code, "anna", null));
        signIn.Code = "123 456";
        signIn.SubmitCommand.Execute(null);

        Assert.Equal(
            [(1, new ConsoleAnswer(Text: "anna")), (2, new ConsoleAnswer(Text: "correct horse")), (3, new ConsoleAnswer(Text: "123 456"))],
            console.Answers);
    }

    [Fact]
    public void ForgetsThePasswordOnceItIsSent()
    {
        TestConsole console = Asked(2, new SignInQuestion(SignInField.Password, "anna", null));
        SignInViewModel signIn = Assert.IsType<SignInViewModel>(console.Model.Question);

        signIn.Password = "correct horse";
        signIn.SubmitCommand.Execute(null);

        Assert.Equal(string.Empty, signIn.Password);

        // Asked again after a wrong one, the field is empty, with the agent's words about it.
        console.Ask(3, new SignInQuestion(SignInField.Password, "anna", "Wrong user name or password."));

        Assert.Equal(string.Empty, signIn.Password);
        Assert.Equal("Wrong user name or password.", signIn.Error);
    }

    [Theory]
    [InlineData(SignInField.Password)]
    [InlineData(SignInField.Code)]
    public void GoesBackFromThePasswordAndTheCodeWithAnEmptyAnswer(SignInField field)
    {
        TestConsole console = Asked(5, new SignInQuestion(field, "anna", null));
        SignInViewModel signIn = Assert.IsType<SignInViewModel>(console.Model.Question);

        Assert.True(signIn.CanGoBack);
        signIn.BackCommand.Execute(null);

        Assert.Equal([(5, new ConsoleAnswer(Text: string.Empty))], console.Answers);
    }

    [Fact]
    public void ChoosesTheSuggestedSequenceFirstAndAnswersWithItsId()
    {
        TestConsole console = Asked(1, Scenarios.Sequences);
        SequenceChoiceViewModel choice = Assert.IsType<SequenceChoiceViewModel>(console.Model.Question);

        Assert.True(choice.Selected!.Option.Suggested);
        Assert.False(choice.CanGoBack);

        choice.Selected = choice.Items[2];
        choice.SubmitCommand.Execute(null);

        Assert.Equal([(1, new ConsoleAnswer(SequenceId: Scenarios.Sequences.Sequences[2].Id))], console.Answers);
    }

    [Fact]
    public void ShowsWhatEachSequenceNeeds()
    {
        TestConsole console = Asked(1, Scenarios.Sequences);
        SequenceChoiceViewModel choice = Assert.IsType<SequenceChoiceViewModel>(console.Model.Question);

        Assert.Equal(
            ["SUGGESTED", "ERASES A DISK", "ASKS FOR A NAME", "NEEDS 40 GB"],
            choice.Items[0].Tags.Select(tag => tag.Text));
        Assert.Contains(choice.Items[2].Tags, tag => tag.Text == "NOT TRUSTED BY THIS MACHINE" && tag.Tone == TagTone.Attention);
    }

    [Fact]
    public void ChoosesADiskOnlyOnPurposeAndGoesBack()
    {
        TestConsole console = Asked(2, Scenarios.Disks);
        DiskChoiceViewModel choice = Assert.IsType<DiskChoiceViewModel>(console.Model.Question);

        // Nothing is chosen for the person, so Enter alone picks no disk.
        Assert.Null(choice.Selected);
        Assert.False(choice.SubmitCommand.CanExecute(null));

        choice.Selected = choice.Items[1];
        choice.SubmitCommand.Execute(null);

        Assert.Equal([(2, new ConsoleAnswer(DiskNumber: 1))], console.Answers);

        TestConsole back = Asked(3, Scenarios.Disks);
        back.Model.Question!.BackCommand.Execute(null);

        Assert.Equal([(3, new ConsoleAnswer(Back: true))], back.Answers);
    }

    [Fact]
    public void DescribesADiskByWhatItIs()
    {
        TestConsole console = Asked(2, Scenarios.Disks);
        DiskItem disk = Assert.IsType<DiskChoiceViewModel>(console.Model.Question).Items[0];

        Assert.Equal(("Disk 0", "Samsung PM9A1 NVMe 512GB", "476.9 GB", "NVMe", "4 partitions"), (disk.Number, disk.Model, disk.Size, disk.Bus, disk.Partitions));
    }

    [Fact]
    public void NamesTheComputerAndTakesTheAgentsWordsAboutAWrongName()
    {
        TestConsole console = Asked(6, Scenarios.ComputerName());
        ComputerNameViewModel name = Assert.IsType<ComputerNameViewModel>(console.Model.Question);

        name.Name = " LAB-PC-0142-WEST ";
        name.SubmitCommand.Execute(null);
        console.Ask(7, Scenarios.ComputerName("A computer name holds at most 15 characters."));

        Assert.Same(name, console.Model.Question);
        Assert.Equal("A computer name holds at most 15 characters.", name.Error);
        Assert.Equal(15, name.MaxLength);

        name.Name = "LAB-PC-0142";
        name.SubmitCommand.Execute(null);
        console.Ask(8, Scenarios.ComputerName());
        name.BackCommand.Execute(null);

        Assert.Equal(
            [(6, new ConsoleAnswer(Text: "LAB-PC-0142-WEST")), (7, new ConsoleAnswer(Text: "LAB-PC-0142")), (8, new ConsoleAnswer(Back: true))],
            console.Answers);
    }

    // The name a rule gives the machine is in the field, so Enter keeps it; what the person types instead goes.
    [Fact]
    public void StartsTheComputerNameWithTheOneTheMachineGetsWithoutOne()
    {
        TestConsole console = Asked(6, Scenarios.ComputerName() with { Name = "PC-00042" });
        ComputerNameViewModel name = Assert.IsType<ComputerNameViewModel>(console.Model.Question);

        Assert.Equal("PC-00042", name.Name);
        Assert.True(name.CanSubmit);

        name.SubmitCommand.Execute(null);

        Assert.Equal([(6, new ConsoleAnswer(Text: "PC-00042"))], console.Answers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("erase")]
    [InlineData("ERAS")]
    [InlineData("ERASE ")]
    public void ErasesNothingUntilTheWordIsTypedExactly(string typed)
    {
        TestConsole console = Asked(7, Scenarios.Erase);
        EraseViewModel erase = Assert.IsType<EraseViewModel>(console.Model.Question);

        erase.Typed = typed;
        erase.SubmitCommand.Execute(null);

        Assert.False(erase.SubmitCommand.CanExecute(null));
        Assert.Empty(console.Answers);
        Assert.Equal(typed.Length >= 5, erase.ShowsWordHint);
    }

    [Fact]
    public void SendsTheWordAsTypedAndGoesBackWithoutIt()
    {
        TestConsole console = Asked(7, Scenarios.Erase);
        EraseViewModel erase = Assert.IsType<EraseViewModel>(console.Model.Question);

        erase.Typed = "ERASE";
        erase.SubmitCommand.Execute(null);

        // The word goes only once; the field empties as it goes.
        erase.SubmitCommand.Execute(null);
        Assert.Equal(string.Empty, erase.Typed);

        TestConsole back = Asked(8, Scenarios.Erase);
        back.Model.Question!.BackCommand.Execute(null);

        Assert.Equal([(7, new ConsoleAnswer(Text: "ERASE"))], console.Answers);
        Assert.Equal([(8, new ConsoleAnswer(Back: true))], back.Answers);
    }

    [Fact]
    public void OverridesSecureBootOnlyWithTheTypedWord()
    {
        TestConsole console = Asked(9, Scenarios.SecureBoot);
        SecureBootViewModel secureBoot = Assert.IsType<SecureBootViewModel>(console.Model.Question);

        Assert.Contains("Microsoft's third-party UEFI CA 2023", secureBoot.Problem, StringComparison.Ordinal);
        Assert.Contains(secureBoot.Facts, fact => fact.Value == "Microsoft's third-party UEFI CAs 2011 and 2023");

        secureBoot.Typed = "anyway";
        Assert.False(secureBoot.SubmitCommand.CanExecute(null));

        secureBoot.Typed = "ANYWAY";
        secureBoot.SubmitCommand.Execute(null);

        Assert.Equal([(9, new ConsoleAnswer(Text: "ANYWAY"))], console.Answers);
    }

    [Fact]
    public void SendsTheInputsOnlyOnceEveryFieldThatNeedsAnAnswerHasOne()
    {
        TestConsole console = Asked(9, Scenarios.Inputs());
        InputsViewModel inputs = Assert.IsType<InputsViewModel>(console.Model.Question);
        TextFieldViewModel owner = Assert.IsType<TextFieldViewModel>(inputs.Fields[1]);
        ChoiceFieldViewModel bitLocker = Assert.IsType<ChoiceFieldViewModel>(inputs.Fields[2]);
        AccountFieldViewModel account = Assert.IsType<AccountFieldViewModel>(inputs.Fields[3]);

        // The choices start with their defaults; the owner and the account have none.
        Assert.Equal("Standard", Assert.IsType<ChoiceFieldViewModel>(inputs.Fields[0]).Selected?.Value);
        Assert.Equal("Yes", bitLocker.Selected?.Label);
        Assert.False(inputs.SubmitCommand.CanExecute(null));

        owner.Text = " anna.berger ";
        account.UserName = @"LAB\svc-join";
        Assert.False(inputs.SubmitCommand.CanExecute(null));

        account.Password = "correct horse";
        bitLocker.Selected = bitLocker.Items[1];
        Assert.True(inputs.SubmitCommand.CanExecute(null));
        inputs.SubmitCommand.Execute(null);

        (int id, ConsoleAnswer answer) = Assert.Single(console.Answers);
        Assert.Equal(9, id);
        Assert.Equal(
            [
                new ConsoleInputValue("Office", "Standard"),
                new ConsoleInputValue("Owner", "anna.berger"),
                new ConsoleInputValue("BitLocker", "false"),
                new ConsoleInputValue("JoinAccount", null, @"LAB\svc-join", "correct horse"),
            ],
            answer.Values);

        // Sent, the password leaves the screen at once.
        Assert.Equal(string.Empty, account.Password);
        Assert.True(inputs.IsSending);
    }

    // Refused, the same page takes the agent's words under the fields: what was typed stays to be put right, but the
    // password is typed again.
    [Fact]
    public void KeepsWhatWasTypedWhenTheAgentAsksAgainButNeverThePassword()
    {
        TestConsole console = Asked(9, Scenarios.Inputs());
        InputsViewModel inputs = Assert.IsType<InputsViewModel>(console.Model.Question);
        ((TextFieldViewModel)inputs.Fields[1]).Text = "annaa";
        AccountFieldViewModel account = (AccountFieldViewModel)inputs.Fields[3];
        account.UserName = @"LAB\svc-join";
        account.Password = "correct horse";
        inputs.SubmitCommand.Execute(null);

        console.Ask(10, Scenarios.Inputs(refused: true));

        Assert.Same(inputs, console.Model.Question);
        Assert.Equal(10, inputs.Id);
        Assert.False(inputs.IsSending);
        Assert.Equal(["Office", "Owner", "Languages", "BitLocker", "JoinAccount"], inputs.Fields.Select(field => field.Input.Name));

        TextFieldViewModel owner = Assert.IsType<TextFieldViewModel>(inputs.Fields[1]);
        AccountFieldViewModel again = Assert.IsType<AccountFieldViewModel>(inputs.Fields[4]);
        Assert.Equal("annaa", owner.Text);
        Assert.Equal("annaa is not a user in lab.local.", owner.Error);
        Assert.Equal(@"LAB\svc-join", again.UserName);
        Assert.Equal(string.Empty, again.Password);
        Assert.Equal("Type the password again.", again.Error);
        Assert.False(inputs.SubmitCommand.CanExecute(null));
    }

    [Fact]
    public void LeavesWhatMayStayEmptyAndJoinsTheChoicesOfAMultipleChoice()
    {
        TestConsole console = Asked(3, new InputsQuestion(
            "Windows 11 24H2 with Office",
            [
                new ConsoleInput("Note", "Note", null, ConsoleInputKind.Text, [], null, false, null, null),
                Scenarios.Inputs(refused: true).Inputs[2],
                new ConsoleInput("Share", "Share account", null, ConsoleInputKind.Account, [], null, false, null, null),
            ],
            null));
        InputsViewModel inputs = Assert.IsType<InputsViewModel>(console.Model.Question);
        MultiChoiceFieldViewModel languages = Assert.IsType<MultiChoiceFieldViewModel>(inputs.Fields[1]);

        Assert.True(inputs.SubmitCommand.CanExecute(null));

        languages.Items[2].IsChosen = true;
        inputs.SubmitCommand.Execute(null);

        Assert.Equal(
            [new ConsoleInputValue("Note", string.Empty), new ConsoleInputValue("Languages", "de-DE;it-IT"), new ConsoleInputValue("Share", null)],
            Assert.Single(console.Answers).Answer.Values);
    }

    // After the pick the list of sequences is a step back; at the start of a run there is none to go back to.
    [Fact]
    public void GoesBackFromTheInputsOnlyAfterThePick()
    {
        TestConsole picked = Asked(9, Scenarios.Inputs());
        picked.Model.Question!.BackCommand.Execute(null);

        Assert.Equal([(9, new ConsoleAnswer(Back: true))], picked.Answers);

        TestConsole waiting = new TestConsole().Show(Scenarios.WaitingForInputs).Ask(9, Scenarios.Inputs());

        Assert.False(waiting.Model.Question!.CanGoBack);
        Assert.False(waiting.Model.Question.BackCommand.CanExecute(null));
    }

    [Fact]
    public void ContinuesThePausedRunAndShowsWhereOnThePathItWaits()
    {
        TestConsole console = new TestConsole().Show(Scenarios.Paused).Ask(11, Scenarios.Pause);
        PauseViewModel pause = Assert.IsType<PauseViewModel>(console.Model.Screen);

        Assert.Equal("Check the BIOS", pause.Title);
        Assert.Equal("PAUSED", pause.Tag.Text);
        Assert.Equal(TagTone.Attention, pause.Tag.Tone);
        Assert.Equal("Pause, step 4 of 8 on this path, in Windows PE", pause.Position);
        Assert.Equal(8, pause.Steps.Count);

        pause.SubmitCommand.Execute(null);

        Assert.Equal([(11, new ConsoleAnswer(Continue: true))], console.Answers);
        Assert.False(pause.CanGoBack);
    }

    private static TestConsole Asked(int id, ConsoleQuestion question)
    {
        ConsoleStage stage = question is SignInQuestion ? ConsoleStage.WaitingForAuthorization : ConsoleStage.Choosing;

        return new TestConsole().Show(Scenarios.State(stage)).Ask(id, question);
    }
}

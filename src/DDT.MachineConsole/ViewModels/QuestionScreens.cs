// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// The screen for each kind of question the agent asks.
public static class QuestionScreens
{
    // Null for a kind of question this console does not know.
    public static QuestionViewModel? Create(
        Localizer l,
        int id,
        ConsoleQuestion question,
        ConsoleState? state,
        Action<int, ConsoleAnswer> answer)
    {
        return question switch
        {
            SignInQuestion signIn => new SignInViewModel(l, id, signIn, state?.Machine.KeyboardLayout, answer),
            SequenceQuestion sequence => new SequenceChoiceViewModel(l, id, sequence, answer),
            DiskQuestion disk => new DiskChoiceViewModel(l, id, disk, answer),
            ComputerNameQuestion computerName => new ComputerNameViewModel(l, id, computerName, answer),
            EraseQuestion erase => new EraseViewModel(l, id, erase, answer),
            SecureBootQuestion secureBoot => new SecureBootViewModel(l, id, secureBoot, state?.Machine.TrustedUefiCas, answer),

            // After a sequence is picked, Back returns to the list. At the start of a run there's no list.
            InputsQuestion inputs => new InputsViewModel(
                l,
                id,
                inputs,
                canGoBack: state?.Stage == ConsoleStage.Choosing,
                state?.Machine.KeyboardLayout,
                answer),
            PauseQuestion pause => new PauseViewModel(l, id, pause, state, answer),
            _ => null,
        };
    }
}

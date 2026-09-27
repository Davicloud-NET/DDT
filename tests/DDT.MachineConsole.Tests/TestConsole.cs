// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Machine;
using DDT.MachineConsole.Texts;
using DDT.MachineConsole.ViewModels;

namespace DDT.MachineConsole.Tests;

// The console's model with what it sends and does recorded instead: answers, closing, restarts.
internal sealed class TestConsole
{
    public TestConsole(UiLanguage language = UiLanguage.English, bool canRestart = false)
    {
        Power = new FakePower(canRestart);
        Model = new MainViewModel(
            Localizer.Embedded(language),
            Power,
            (id, answer) => Answers.Add((id, answer)),
            () => Closed++);
    }

    public MainViewModel Model { get; }

    public FakePower Power { get; }

    public List<(int Id, ConsoleAnswer Answer)> Answers { get; } = [];

    public int Closed { get; private set; }

    public TestConsole Receive(params ConsoleMessage[] messages)
    {
        foreach (ConsoleMessage message in messages)
        {
            Model.Receive(message);
        }

        return this;
    }

    public TestConsole Show(ConsoleState state) => Receive(new StateMessage(state));

    public TestConsole Ask(int id, ConsoleQuestion question) => Receive(new QuestionMessage(id, question));
}

internal sealed class FakePower(bool canRestart) : IMachinePower
{
    public bool IsWindowsPE => canRestart;

    public bool CanRestart => canRestart;

    public int Restarts { get; private set; }

    public void Restart() => Restarts++;
}
